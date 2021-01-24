using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace UdpFile
{
    public class UdpTransportClientConfig
    {
        public FileInfo SrcFileInfo;
        public IPEndPoint TargetAddress;
        public string TargetFileName;
        public int BlockSize;
        public OverrideModeEnum Mode;
        public int CmdSentCount;
    }
    public static class UdpFileClient
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static async Task EnsureCmdSent(UdpClient udpClient, byte[] buf, int count, IPEndPoint target,
            int sentCount)
        {
            for (var i = 0; i < sentCount; i++)
            {
                await udpClient.SendAsync(buf, count, target);
            }
        }
        
        public static async Task Sent(UdpTransportClientConfig cfg)
        {
            using var blockReader = new FileBlockReader(cfg.BlockSize, cfg.SrcFileInfo);
            var udpClient = new UdpClient();
            var sentCount = 0;
            var cmd = new CommandPackage();
            
            {
                var startInfo = new StartCommandInfo
                {
                    BlockSize = cfg.BlockSize, TargetFileSize = cfg.SrcFileInfo.Length,
                    Version = 1,OverriteMode = cfg.Mode
                };
                var (buf, count) = PackageBuilder.PrepareStartPack(ref cmd, ref startInfo, cfg.TargetFileName);
                await EnsureCmdSent(udpClient, buf, count, cfg.TargetAddress, cfg.CmdSentCount);
            }

            var hashTask = CalAndSentHashAsync(cfg, blockReader);
            
            var dataInfo = new DataCommandInfo();
            for (long i = 0; i < blockReader.MaxBlockIndex; i++)
            {
                dataInfo.BlockIndex = i;
                var (buf, count) = PackageBuilder.PrepareDataPack(ref cmd, ref dataInfo, blockReader);
                sentCount += await udpClient.SendAsync(buf, count, cfg.TargetAddress);
            }

            await hashTask;
            {
                var stopInfo = new StopCommandInfo();
                var (buf, count) = PackageBuilder.PrepareStopPack(ref cmd, ref stopInfo);
                await EnsureCmdSent(udpClient, buf, count, cfg.TargetAddress, cfg.CmdSentCount);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
        }

        private static async Task CalAndSentHashAsync(UdpTransportClientConfig cfg, FileBlockReader blockReader)
        {
            var lastTask = Task.Delay(1);
            await lastTask;
            var udpClient = new UdpClient();
            var cmd = new CommandPackage {Cmd = CommandEnum.Verify};
            var vPack = new VerifyCommandInfo {Length = blockReader.HasherLen};
            
            for (long i = 0; i < blockReader.MaxBlockIndex; i++)
            {
                var hashBuf = blockReader.CalculateHash(i);
                //PrepareHashPack not thread safe, make sure the internal buffer is sent
                await lastTask;
                var (buf, count) = PackageBuilder.PrepareHashPack(ref cmd, ref vPack, hashBuf, i);
                lastTask = EnsureCmdSent(udpClient, buf, count, cfg.TargetAddress, cfg.CmdSentCount);
            }
        }
    }
}