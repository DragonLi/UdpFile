using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UdpFile
{
    public class UdpFileTransportController
    {
        //TODO read from config
        private const int BufSize = 4*1024;

        public static async Task Sent(FileInfo srcFileInfo, IPEndPoint targetAddress, string targetFsNm)
        {
            using var blockReader = new FileBlockReader(BufSize, srcFileInfo);
            var udpClient = new UdpClient();
            var sentCount = 0;
            foreach (var (buf,count,index) in blockReader)
            {
                sentCount += await udpClient.SendAsync(buf, count, targetAddress);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
        }
    }
}