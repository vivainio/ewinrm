using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Console;
namespace EWinRM
{
    public class ProtocolError : Exception
    {
        public ProtocolError(string message) : base(message)
        {
        }
    }
    public static class RespProtocol
    {
        static byte[] ReadUntilByte(BinaryReader src, byte sentinel)
        {
            var bytes = new List<byte>();
            while (true)
            {
                var b = src.ReadByte();
                if (b == sentinel)
                {
                    return bytes.ToArray();
                }
                bytes.Add(b);
            }
        }

        static string ReadLine(BinaryReader src)
        {
            var bytes = ReadUntilByte(src, (byte)'\n');

            if (bytes[bytes.Length-1] == '\r')
            {
                Array.Resize(ref bytes, bytes.Length - 1);
            }
            return UTF8Encoding.UTF8.GetString(bytes);
        }

        static void AssertPrefix(string s, char ch)
        {
            if (s[0] != ch)
            {
                throw new ProtocolError($"Expected '{ch}' got {s[0]})");               
            }
        }
        public static void ReadBulkString(BinaryReader src, BinaryWriter dest)
        {
            // It starts with $ + length + \r\n
            var line = ReadLine(src);

            AssertPrefix(line, '$');
            var numstring = Int32.Parse(line.Substring(1).TrimEnd());
            PsUtil.CopyStreamNBytes(dest, src, numstring);
        }
        public static string ReadSimpleString(BinaryReader src)
        {
            // starts with +
            var line = ReadLine(src);
            AssertPrefix(line, '+');

            return line.Substring(1);
        }
    }
    class PsUtil
    {
        public static void CopyStreamNBytes(BinaryWriter dest, BinaryReader src, int nbytes)
        {
            byte[] buf = new byte[1024];

            var remaining = nbytes;
            while (remaining > 0)
            {
                var toread = Math.Min(remaining, buf.Length);
                int len = src.Read(buf, 0, toread);
                dest.Write(buf, 0, len);
                remaining -= len;
                if (remaining == 0)
                {
                    // all done!
                    break;
                }
            }
        }


        public static void CopyStreamAsUtf(BinaryWriter dest, StreamReader src)
        {
            char[] buf = new char[1024];
            
            while (!src.EndOfStream)
            {
                int len = src.Read(buf, 0, buf.Length);
                dest.Write(buf, 0, len);
            }
        }


        public static int RunProcess(BinaryWriter outstream, Action<ProcessStartInfo> decorateFn)
        {
            var p = new Process();
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.UseShellExecute = false;
            decorateFn(p.StartInfo);
            p.Start();
            CopyStreamAsUtf(outstream, p.StandardOutput);
            CopyStreamAsUtf(outstream, p.StandardError);
            outstream.Flush();
            p.WaitForExit();
            return p.ExitCode;            
        }
    }

    class TempFileUtil
    {
        public static string WorkPath()
        {
            var p = Path.Combine(Path.GetTempPath(), "EWinRmFiles");
            if (!Directory.Exists(p))
            {
                Directory.CreateDirectory(p);
            }
            return p;
        }

        public static string CreateTempDir()
        {
            var p = Path.Combine(WorkPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(p);
            return p;
        }

        public static string GetTempFileName() => Path.Combine(WorkPath(), Path.GetRandomFileName());
    }

    class Program
    {

        public static void RunProcessAndWriteExitCode(BinaryWriter outstream, Action<ProcessStartInfo> decorateFn)
        {
            int code = PsUtil.RunProcess(outstream, decorateFn);
            if (code != 0)
            {
                outstream.Write($"\nEE EXITCODE: {code}.");

            }
        }

        static void HandleZipFile(string pth, BinaryWriter resp)
        {
            var targetDir = TempFileUtil.CreateTempDir();
            Console.WriteLine($"Unzip {pth} to {targetDir}");

            try
            {
                ZipFile.ExtractToDirectory(pth, targetDir);
            } catch (InvalidDataException ex) {
                resp.Write($"EE: invalid zip file: {ex.Message}");
                return;
            }
            var autoexec = Path.Combine(targetDir, "autoexec.cmd");
            if (!File.Exists(autoexec))
            {
                resp.Write("EE: Missing autoexec.cmd from zip");
                return;

            }

            RunProcessAndWriteExitCode(resp, psi =>
            {
                psi.WorkingDirectory = targetDir;
                psi.FileName = "cmd";
                psi.Arguments = "/c autoexec.cmd";
            });


        }

        static void HandleStoredFile(string pth, BinaryWriter resp)
        {
            string prefix;
            using (var f = File.OpenRead(pth))
            {
                byte[] prefixBuf = new byte[3];
                int len = f.Read(prefixBuf, 0, 3);
                if (len < 3) {
                    resp.Write("EE: Script must have at least 3 characters");
                    return;
                }
                prefix = Encoding.ASCII.GetString(prefixBuf);
            }
            HandleScriptFile(prefix, pth, resp);
        }

        private static void HandleScriptFile(string prefix, string pth, BinaryWriter resp)
        {
            // python 
            if (prefix.StartsWith("#!p"))
            {
                RunProcessAndWriteExitCode(resp, psi =>
                {
                    psi.FileName = "python";
                    psi.Arguments = pth;
                });
                return;
            }

            // unknown profix, let's handle it as bat
            var newName = pth + ".cmd";
            File.Move(pth, newName);

            RunProcessAndWriteExitCode(resp, psi =>
            {
                psi.FileName = "cmd";
                psi.Arguments = "/c " + newName;
            });

            File.Delete(newName);
        }


        static void HandleSocketOuter(Object sock)
        {
            var s = sock as Socket;
            try
            {
                HandleSocket(sock as Socket);
            } catch (Exception ex)
            {

                using (var writer = new StreamWriter(new NetworkStream(s)))
                {
                    writer.WriteLine(ex.Message);
                    writer.Flush();
                }
                
            }
        }


        static void HandleSocket(Socket sock)
        {
            var networkStream = new BufferedStream(new NetworkStream(sock));
            var writer = new BinaryWriter(networkStream);
            var reader = new BinaryReader(networkStream);

            var pth = TempFileUtil.GetTempFileName();

            void copyBulkStreamToNewFile(string fname)
            {
                using (var f = new BinaryWriter(File.OpenWrite(pth)))
                {
                    RespProtocol.ReadBulkString(reader, f);
                }
            }

            while (true)
            {
                // main command - handle loop
                var cmd = RespProtocol.ReadSimpleString(reader);
                Console.WriteLine($"Command: {cmd}");
                // 1: run simple script
                if (cmd == "run")
                {
                    copyBulkStreamToNewFile(pth);
                    HandleStoredFile(pth, writer);
                    writer.Close();
                    break;

                }
                if (cmd == "ziprun")
                {
                    copyBulkStreamToNewFile(pth);
                    HandleZipFile(pth, writer);
                    writer.Close();
                    break;
                }
                Console.WriteLine($"Unknown command: {cmd}");


            }
        }

        class AppConfig
        {
            public int Port;
            public string Password; 

        }

        static AppConfig Config = new AppConfig
        {
            Port = 19802,
            Password = "helo"
        };

        static async Task Loop()
        {
            var port = Config.Port;
            Console.WriteLine($"EWinRM listening for TCP on port {port}");
            var t = new TcpListener(IPAddress.Any, port);
            t.Start();
            while (true)
            {
                var sock = await t.AcceptSocketAsync();
                new Thread(new ParameterizedThreadStart(HandleSocketOuter)).Start(sock);
            }
        }
        static void Main(string[] args)
        {
            var t = Task.Run(Loop);
            t.Wait();
        }
    }
}
