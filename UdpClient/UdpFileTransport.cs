using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace UdpFile
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            var fsNm = GetArgs(args,0);
            if (!File.Exists(fsNm))
            {
                Console.Out.WriteLine($"File not exists: {fsNm}");
                return;
            }

            var ip = GetArgs(args,1);
            if (!IPEndPoint.TryParse(ip, out var targetAddress))
            {
                Console.Out.WriteLine($"Ip not valid: {ip}");
                return;
            }

            var targetFsNm = GetArgs(args,2);
            try
            {
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
            var srcFileInfo = new FileInfo(fsNm);
            #if TestLocal
            LocalMemoryFileTest(srcFileInfo, targetAddress, targetFsNm);
            #endif
            var watcher = Stopwatch.StartNew();
            watcher.Start();
            await UdpFileClient.Sent(srcFileInfo,targetAddress,targetFsNm);
            watcher.Stop();
            Console.Out.WriteLine($"finished with time: {watcher.Elapsed}");
        }

        private static IPAddress GetIpAddress(string ip)
        {
            if (ip.Equals("localhost"))
                return IPAddress.Loopback;
            return IPAddress.Parse(ip);
        }
        private static string GetArgs(string[] args, int i) => i < args.Length ? args[i] : string.Empty;
        private const int BufSize = 4*1024;

        static void LocalMemoryFileTest(FileInfo fsNm, IPAddress targetAddress, string targetFsNm)
        {
            using var blockReader = new FileBlockReader(BufSize, fsNm);
            using var dumper = new FileBlockDumper(targetFsNm,BufSize,fsNm.Length);
            foreach (var (buf,count,index) in blockReader)
            {
                LogArray(buf, count,index);
                dumper.WriteBlock(index, buf, count);
            }
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
            diffProc.WaitForExit();
        }

        private static void LogArray(byte[] buf, int readCount, long index)
        {
            var lineCharNum = 8;
            var start = index * BufSize;
            var lineCharIndex = 0;
            for (int i = 0; i < readCount; i++,++start)
            {
                if (lineCharIndex == 0)
                {
                    Console.Out.Write($"{start:x8}: ");
                }
                if (lineCharIndex < lineCharNum)
                {
                    Console.Out.Write($"{buf[i]:x2}");
                    if (start % 2 == 1)
                    {
                        Console.Out.Write(' ');
                    }
                }

                lineCharIndex++;
                if (lineCharIndex == lineCharNum)
                {
                    lineCharIndex = 0;
                    Console.Out.WriteLine();
                }
            }
        }
    }
}