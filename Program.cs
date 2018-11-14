using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
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
    class Program
    {

        static string WorkPath()
        {
            var p = Path.Combine(Path.GetTempPath(), "EpicWinRmFiles");
            if (!Directory.Exists(p))
            {
                Directory.CreateDirectory(p);
            }
            return p;
        }

        static string CreateTempDir()
        {
            var p = Path.Combine(WorkPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(p);
            return p;
        }



        static void HandleZipFile(string pth, StreamWriter resp)
        {
            var targetDir = CreateTempDir();
            Console.WriteLine($"Unzip {pth} to {targetDir}");

            ZipFile.ExtractToDirectory(pth, targetDir);
        }

        static void HandleStoredFile(string pth, StreamWriter resp)
        {
            var iszip = false;
            using (var f = File.OpenRead(pth))
            {
                byte[] prefix = new byte[2];
                int len = f.Read(prefix, 0, 2);
                if (prefix[0] == 'P' && prefix[1] == 'K')
                {
                    iszip = true;
                }
            }
            if (iszip)
            {
                HandleZipFile(pth, resp);
            } else
            {
                HandleBatFile(pth, resp);
            }
        }

        private static void HandleBatFile(string pth, StreamWriter resp)
        {
            var newName =   pth + ".cmd";
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
            var pth = Path.GetTempFileName();

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

                var responseStream = new StreamWriter(new NetworkStream(s));
                HandleStoredFile(pth, responseStream);
                s.Close();

            } catch (SocketException)
            {
                // ...
            } finally
            {
                //File.Delete(pth);
            }

        }

        static async Task Loop()
        {
            var t = new TcpListener(IPAddress.Any, 19800);
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
