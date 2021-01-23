using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UdpFile
{
    public class UdpTransportClientConfig
    {
        public FileInfo srcFileInfo;
        public IPEndPoint targetAddress;
        public string targetFileName;
        public int blockSize;
        public OverrideModeEnum mode;
    }
    public class UdpFileClient
    {
        static async Task EnsureCmdSent(UdpClient udpClient, byte[] buf, int count, IPEndPoint target)
        {
            await udpClient.SendAsync(buf, count, target);
            await udpClient.SendAsync(buf, count, target);
        }
        
        public static async Task Sent(UdpTransportClientConfig cfg)
        {
            using var blockReader = new FileBlockReader(cfg.blockSize, cfg.srcFileInfo);
            var udpClient = new UdpClient();
            var sentCount = 0;
            var cmd = new CommandPackage();
            {
                var startInfo = new StartCommandInfo
                {
                    BlockSize = cfg.blockSize, TargetFileSize = cfg.srcFileInfo.Length,
                    Version = 1,OverriteMode = cfg.mode
                };
                var (buf, count) = PackageBuilder.PrepareStartPack(ref cmd, ref startInfo, cfg.targetFileName);
                await EnsureCmdSent(udpClient, buf, count, cfg.targetAddress);
            }
            
            var dataInfo = new DataCommandInfo();
            for (long i = 0; i < blockReader.MaxBlockIndex; i++)
            {
                dataInfo.BlockIndex = i;
                var (buf, count) = PackageBuilder.PrepareDataPack(ref cmd, ref dataInfo, blockReader);
                sentCount += await udpClient.SendAsync(buf, count, cfg.targetAddress);
            }

            {
                var stopInfo = new StopCommandInfo();
                var (buf, count) = PackageBuilder.PrepareStopPack(ref cmd, ref stopInfo);
                await EnsureCmdSent(udpClient, buf, count, cfg.targetAddress);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
        }
    }
}