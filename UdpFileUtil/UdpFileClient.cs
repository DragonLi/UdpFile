using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UdpFile
{
    public class UdpFileClient
    {
        //TODO read from config
        private const int BufSize = 4*1024;

        public static async Task Sent(FileInfo srcFileInfo, IPEndPoint targetAddress, string targetFsNm)
        {
            using var blockReader = new FileBlockReader(BufSize, srcFileInfo);
            var udpClient = new UdpClient();
            var sentCount = 0;
            var cmd = new CommandPackage();
            cmd.SeqId = TransportSeqFactory.NextId();
            udpClient.SendAsync()
            var info = new DataCommandInfo();
            for (long i = 0; i < blockReader.MaxBlockIndex; i++)
            {
                cmd.SeqId = TransportSeqFactory.NextId();
                info.BlockIndex = i;
                var (buf, count) = blockReader.QuickPrepare(ref cmd, ref info);
                sentCount += await udpClient.SendAsync(buf, count, targetAddress);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
        }
    }
}