using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EWinRM
{
    class PsUtil
    {
       
        static void CopyStream(StreamWriter dest, StreamReader src)
        {
            char[] buf = new char[1024];
            
            while (true)
            {
                int len = src.Read(buf, 0, buf.Length);
                if (len == 0)
                {
                    break;
                }
                dest.Write(buf, 0, len);
            }
        }

        public static void RunProcess(StreamWriter outstream, Action<ProcessStartInfo> decorateFn)
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
            outstream.Close();
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

            PsUtil.RunProcess(resp, psi =>
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
                byte[] prefixBuf = new byte[2];
                int len = f.Read(prefixBuf, 0, 2);
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

            // unknown profix, let's handle it as bat
            var newName = pth + ".cmd";
            File.Move(pth, newName);

            PsUtil.RunProcess(resp, psi =>
            {
                psi.FileName = "cmd";
                psi.Arguments = "/c " + newName;
            });

            File.Delete(newName);            
        }

        static void HandleSocket(Object sock)
        {

            var pth = TempFileUtil.GetTempFileName();

            byte[] buf = new byte[1024];
            var s = sock as Socket;
            try
            {
                using (var f = File.OpenWrite(pth))
                {
                    while (true)
                    {
                        int len = s.Receive(buf, SocketFlags.None);
                        Console.WriteLine($"Got {len}");
                        f.Write(buf, 0, len);
                        if (len < buf.Length)
                        {
                            break;
                        }

                    }
                }
                Console.WriteLine(pth);

                using (var responseStream = new StreamWriter(new NetworkStream(s)))
                {
                    HandleStoredFile(pth, responseStream);
                }
                s.Close();
            }
            catch (SocketException)
            {
                // ...
            } finally
            {
                //File.Delete(pth);
            }

        }

        static async Task Loop()
        {
            var port = 19800;
            Console.WriteLine("EWinRM listening for TCP on port 19800");
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
