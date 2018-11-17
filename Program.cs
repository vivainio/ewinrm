using System;
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

        static void AssertPrefix(string s, char ch)
        {
            if (s[0] != ch)
            {
                throw new ProtocolError($"Expected '{ch}' got {s[0]})");               
            }
        }
        public static void ReadBulkString(StreamReader src, StreamWriter dest)
        {
            // It starts with $ + length + \r\n
            var line = src.ReadLine();
            AssertPrefix(line, '$');
            var numstring = Int32.Parse(line.Substring(1).TrimEnd());
            PsUtil.CopyStreamNBytes(dest, src, numstring);
        }
        public static string ReadSimpleString(StreamReader src)
        {
            // starts with +
            var line = src.ReadLine();
            AssertPrefix(line, '+');

            return line.Substring(1);
        }
    }
    class PsUtil
    {
        public static void CopyStreamNBytes(StreamWriter dest, StreamReader src, int nbytes)
        {
            char[] buf = new char[1024];

            var remaining = nbytes;
            while (!src.EndOfStream)
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


        public static void CopyStream(StreamWriter dest, StreamReader src)
        {
            char[] buf = new char[1024];
            
            while (!src.EndOfStream)
            {
                int len = src.Read(buf, 0, buf.Length);
                
                dest.Write(buf, 0, len);
            }
        }


        public static int RunProcess(StreamWriter outstream, Action<ProcessStartInfo> decorateFn)
        {
            var p = new Process();
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.UseShellExecute = false;
            decorateFn(p.StartInfo);
            p.Start();
            CopyStream(outstream, p.StandardOutput);
            CopyStream(outstream, p.StandardError);
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

        public static void RunProcessAndWriteExitCode(StreamWriter outstream, Action<ProcessStartInfo> decorateFn)
        {
            int code = PsUtil.RunProcess(outstream, decorateFn);
            if (code != 0)
            {
                outstream.Write($"\nEE EXITCODE: {code}.");

            }
        }

        static void HandleZipFile(string pth, StreamWriter resp)
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

        static void HandleStoredFile(string pth, StreamWriter resp)
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

        private static void HandleScriptFile(string prefix, string pth, StreamWriter resp)
        {

            // PK prefix is zip file
            if (prefix.StartsWith("PK"))
            {
                HandleZipFile(pth, resp);
                return;

            }

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

        static int JobNumber = 0;
        static string CreateJob()
        {
            ++JobNumber;
            return "job" + JobNumber;

        }
        static void HandleSocket(Object sock)
        {
            var networkStream = new NetworkStream(sock as Socket);
            var writer = new StreamWriter(networkStream);
            var reader = new StreamReader(networkStream);

            var pth = TempFileUtil.GetTempFileName();

            void copyBulkStreamToNewFile(string fname)
            {
                using (var f = File.OpenWrite(pth))
                using (var fwriter = new StreamWriter(f))
                {
                    RespProtocol.ReadBulkString(reader, fwriter);
                }
            }

            while (true)
            {
                // main command - handle loop
                var cmd = RespProtocol.ReadSimpleString(reader);
                // 1: run simple script
                if (cmd == "run")
                {
                    copyBulkStreamToNewFile(pth);
                    HandleStoredFile(pth, writer);
                }
                if (cmd == "ziprun")
                {
                    copyBulkStreamToNewFile(pth);
                    HandleZipFile(pth, writer);
                }


            }
        }

        class AppConfig
        {
            public int Port;
            public string Password; 

        }

        static AppConfig Config = new AppConfig
        {
            Port = 19800,
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
                new Thread(new ParameterizedThreadStart(HandleSocket)).Start(sock);
            }
        }
        static void Main(string[] args)
        {
            var t = Task.Run(Loop);
            t.Wait();
        }
    }
}
