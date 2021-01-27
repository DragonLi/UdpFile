using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace UdpFile
{
    internal static class ConsoleDriverClient
    {
        private static async Task Main(string[] args)
        {
            var fsNm = GetArgs(args,0);
            if (!File.Exists(fsNm))
            {
                Logger.Err($"File not exists: {fsNm}");
                return;
            }

            var ip = GetArgs(args,1);
            if (!IPEndPoint.TryParse(ip, out var targetAddress))
            {
                Logger.Err($"Ip not valid: {ip}");
                return;
            }

            var targetFsNm = GetArgs(args,2);
            try
            {
                if (Path.EndsInDirectorySeparator(targetFsNm))
                {
                    Logger.Err($"target file name must not be a directory: {GetArgs(args,2)}");
                    return;
                }
            }
            catch (Exception e)
            {
                Logger.Err($"Invalid target file name: {GetArgs(args,2)}");
                Console.WriteLine(e);
                return;
            }

            var blockSizeInK = GetArgs(args, 3);
            if (!int.TryParse(blockSizeInK, out var blockSize))
            {
                blockSize = 4;
            }

            var modeStr = GetArgs(args, 4);
            if (!int.TryParse(modeStr,out var modeInt))
            {
                modeInt = 0;
            }

            var mode = EnumHelper.FromInt(modeInt);

            Logger.Info($"{fsNm},{ip},{targetFsNm},{blockSize},{mode}");
            var srcFileInfo = new FileInfo(fsNm);
            var cfg = new UdpTransportClientConfig()
            {
                BlockSize = blockSize*1024, SrcFileInfo = srcFileInfo,
                TargetAddress = targetAddress, TargetFileName = targetFsNm,
                Mode = mode,CmdSentCount = 2,LocalPort=9998
            };
            #if TestLocal
            LocalMemoryFileTest(srcFileInfo, targetAddress, targetFsNm);
            TestHasher(cfg);
            #endif
            var watcher = Stopwatch.StartNew();
            watcher.Start();
            await UdpFileClient.Sent(cfg);
            watcher.Stop();
            Logger.Info($"finished with time: {watcher.Elapsed}");
        }

        private static void TestHasher(UdpTransportClientConfig cfg)
        {
            using var blockReader = new FileBlockReader(cfg.BlockSize, cfg.SrcFileInfo);
            for (var i = 0; i < blockReader.MaxBlockIndex; i++)
            {
                var hashBuf = blockReader.CalculateHash(i);
                Console.Write($"{i}: ");
                foreach (var b in hashBuf)
                {
                    Console.Write($"{b:X2} ");
                }
                Console.WriteLine();
            }
            for (var i = blockReader.MaxBlockIndex - 1; i >= 0; i--)
            {
                var hashBuf = blockReader.CalculateHash(i);
                Console.Write($"{i}: ");
                foreach (var b in hashBuf)
                {
                    Console.Write($"{b:X2} ");
                }
                Console.WriteLine();
            }
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
            Console.WriteLine($"run: diff -s {targetFsNm} {fsNm}");
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

        private static void LogArray(byte[] buf, int readCount, int index)
        {
            var lineCharNum = 8;
            long start = index * BufSize;
            var lineCharIndex = 0;
            for (var i = 0; i < readCount; i++,++start)
            {
                if (lineCharIndex == 0)
                {
                    Console.Write($"{start:x8}: ");
                }
                if (lineCharIndex < lineCharNum)
                {
                    Console.Write($"{buf[i]:x2}");
                    if (start % 2 == 1)
                    {
                        Console.Write(' ');
                    }
                }

                lineCharIndex++;
                if (lineCharIndex == lineCharNum)
                {
                    lineCharIndex = 0;
                    Console.WriteLine();
                }
            }
        }
    }
}