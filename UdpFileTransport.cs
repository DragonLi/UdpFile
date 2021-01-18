using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace UdpFile
{
    static class Program
    {
        static void Main(string[] args)
        {
            var fsNm = GetArgs(args,0);
            if (!File.Exists(fsNm))
            {
                Console.Out.WriteLine($"File not exists: {fsNm}");
                return;
            }

            var ip = GetArgs(args,1);
            if (!IPAddress.TryParse(ip, out var targetAddress))
            {
                Console.Out.WriteLine($"Ip not valid: {ip}");
                return;
            }

            var targetFsNm = GetArgs(args,2);
            try
            {
                targetFsNm = Path.GetFullPath(targetFsNm);
                if (Path.EndsInDirectorySeparator(targetFsNm))
                {
                    Console.Out.WriteLine($"target file name must not be a directory: {GetArgs(args,2)}");
                    return;
                }
            }
            catch (Exception e)
            {
                Console.Out.WriteLine($"Invalid target file name: {GetArgs(args,2)}");
                Console.WriteLine(e);
                return;
            }

            Console.Out.WriteLine($"{fsNm},{ip},{targetFsNm}");
            var watcher = Stopwatch.StartNew();
            watcher.Start();
            UdpFileTransportController.Sent(new FileInfo(fsNm),targetAddress,targetFsNm);
            watcher.Stop();
            Console.Out.WriteLine($"finished with time: {watcher.Elapsed}");
            Console.Out.WriteLine($"run: diff -s {targetFsNm} {fsNm}");
            using var diffProc = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    Arguments = $"-s {targetFsNm} {fsNm}",
                    FileName = "diff",
                    CreateNoWindow = true
                }
            };
            diffProc.Start();

            targetAddress = GetIpAddress("localhost");
            Console.Out.WriteLine(targetAddress);
        }

        private static IPAddress GetIpAddress(string ip)
        {
            if (ip.Equals("localhost"))
                return IPAddress.Loopback;
            return IPAddress.Parse(ip);
        }
        private static string GetArgs(string[] args, int i) => i < args.Length ? args[i] : string.Empty;
    }
}