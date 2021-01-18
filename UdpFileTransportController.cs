using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UdpFile
{
    public class UdpFileTransportController
    {
        private const int BufSize = 4*1024;

        public static async Task Sent(FileInfo fsNm, IPAddress targetAddress, string targetFsNm)
        {
            using var blockReader = new FileBlockReader(BufSize, fsNm);
            using var dumper = new FileBlockDumper(targetFsNm,BufSize,fsNm.Length);
            var total = fsNm.Length;
            var max = total / BufSize;
            max = total - max * BufSize > 0 ? max + 1 : max;
            //reverse order to test memory map file randomly write function
            for (var i = (int) (max -1); i >= 0; --i)
            {
                var (buf,readCount) = blockReader.Read(i);
                dumper.WriteBlock(i, buf, readCount);
            }

            var udpClient = new UdpClient();
            IPEndPoint ep = new IPEndPoint(IPAddress.Loopback, 9999);
            var sentCount = 0;
            foreach (var (buf,count,index) in blockReader)
            {
                //LogArray(buf, count,index);
                sentCount += await udpClient.SendAsync(buf, count, ep);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
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