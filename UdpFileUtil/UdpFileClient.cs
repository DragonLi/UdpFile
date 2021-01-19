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
            {
                var startInfo = new StartCommandInfo {BlockSize = BufSize, TargetFileSize = srcFileInfo.Length};
                var (buf, count) = PackageBuilder.PrepareStartPack(ref cmd, ref startInfo, targetFsNm);
                await udpClient.SendAsync(buf, count, targetAddress);
            }
            
            var dataInfo = new DataCommandInfo();
            for (long i = 0; i < blockReader.MaxBlockIndex; i++)
            {
                dataInfo.BlockIndex = i;
                var (buf, count) = PackageBuilder.PrepareDataPack(ref cmd, ref dataInfo, blockReader);
                sentCount += await udpClient.SendAsync(buf, count, targetAddress);
            }

            {
                var stopInfo = new StopCommandInfo();
                var (buf, count) = PackageBuilder.PrepareStopPack(ref cmd, ref stopInfo);
                await udpClient.SendAsync(buf, count, targetAddress);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
        }
    }
}