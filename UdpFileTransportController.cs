using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;

namespace UdpFile
{
    public class UdpFileTransportController
    {
        private const int BufSize = 4*1024;
        private static readonly byte[] MmfBuf = new byte[BufSize];
        public static void Sent(FileInfo fsNm, IPAddress targetAddress, string targetFsNm)
        {
            using var mmf = MemoryMappedFile.CreateFromFile(fsNm.FullName);
            using var accessor = mmf.CreateViewAccessor();
            using var dumper = new FileDumper(targetFsNm,BufSize,fsNm.Length);
            var total = fsNm.Length;
            var max = total / BufSize;
            max = total - max * BufSize > 0 ? max + 1 : max;
            for (var i = 0; i < max; i++)
            {
                var readCount = accessor.ReadArray(i*BufSize, MmfBuf, 0, MmfBuf.Length);
                LogArray(MmfBuf, readCount,i);
                dumper.WriteBlock(i, MmfBuf, readCount);
            }
        }

        private static void LogArray(byte[] buf, int readCount, int index)
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