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
        public int LocalPort;
    }
    public static class UdpFileClient
    {
        public static async Task Sent(UdpTransportClientConfig cfg)
        {
            using var blockReader = new FileBlockReader(cfg.BlockSize, cfg.SrcFileInfo);
            var udpClient = new UdpClient();
            var udpReceived = new UdpClient(cfg.LocalPort);
            var sentCount = 0;
            var cmd = new CommandPackage();
            
            {
                var startInfo = new StartCommandInfo
                {
                    BlockSize = cfg.BlockSize, TargetFileSize = cfg.SrcFileInfo.Length,
                    Version = 1,OverrideMode = cfg.Mode,ClientPort = cfg.LocalPort
                };
                var (buf, count) = PackageBuilder.PrepareStartPack(ref cmd, ref startInfo, cfg.TargetFileName);
                if (!await StartServerAsync(udpClient, buf, count, cfg, udpReceived, cmd.SeqId))
                {
                    udpReceived.Close();
                    udpClient.Close();
                    return;
                }
            }

            var hashTask = CalAndSentHashAsync(cfg, blockReader, udpReceived);
            
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
                await udpClient.EnsureCmdSent(buf, count, cfg.TargetAddress, cfg.CmdSentCount);
            }
            await Console.Out.WriteLineAsync($"sent count: {sentCount}");
            udpClient.Close();
            udpReceived.Close();
        }

        private static async Task<bool> StartServerAsync(UdpClient udpClient, byte[] sentBuf, int count,
            UdpTransportClientConfig cfg, UdpClient udpReceived, int cmdSeqId)
        {
            await udpClient.EnsureCmdSent(sentBuf, count, cfg.TargetAddress, cfg.CmdSentCount);
            const int maxTimeoutCount = 3;
            const int timeoutInterval = 3 * 1000;
            int timeoutCount = 0;
            do
            {
                var task = udpReceived.ReceiveAsync();
                var timeOutSignal = Task.WaitAny(new Task[] {task}, timeoutInterval);
                if (timeOutSignal < 0)
                {
                    timeoutCount++;
                    await udpClient.EnsureCmdSent(sentBuf, count, cfg.TargetAddress, cfg.CmdSentCount);
                    continue;
                }

                var result = await task;
                if (!result.RemoteEndPoint.Address.Equals(cfg.TargetAddress.Address))
                {
                    Logger.Warn($"unexpected client: {result.RemoteEndPoint}");
                    continue;
                }

                var buf = result.Buffer;
                CommandPackage receivedCmd = new();
                StartAckInfo ack = new();
                var offset = receivedCmd.ReadFrom(buf, 0);
                if (offset <= 0)
                {
                    Logger.Warn($"invalid command header");
                    continue;
                }

                if (receivedCmd.Cmd != CommandEnum.StartAck)
                {
                    Logger.Warn($"expect start ack but {receivedCmd.Cmd} received");
                    continue;
                }
                var renameTo = ack.ReadFrom(buf, offset, out var ackSuccess);
                if (!ackSuccess)
                {
                    Logger.Warn("failed to decode start ack package");
                    continue;
                }
                if (ack.AckSeqId != cmdSeqId)
                {
                    Logger.Warn($"start ack unexpected seq id: {ack.AckSeqId}");
                    continue;
                }
                
                if (renameTo.Length > 0)
                {
                    Logger.Info($"target file rename to: {renameTo}");
                    cfg.TargetFileName = renameTo;
                }

                if (ack.Port != cfg.TargetAddress.Port)
                {
                    Logger.Info($"target address port change to {ack.Port}");
                    cfg.TargetAddress.Port = ack.Port;
                }
                return true;
            } while (timeoutCount < maxTimeoutCount);
            Logger.Warn($"start command not confirmed: timeout");
            return false;
        }

        private static async Task CalAndSentHashAsync(UdpTransportClientConfig cfg, FileBlockReader blockReader,
            UdpClient udpReceived)
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
                lastTask = udpClient.EnsureCmdSent(buf, count, cfg.TargetAddress, cfg.CmdSentCount);
            }
            //ask for confirm
        }
    }
}