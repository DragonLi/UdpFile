using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
        public const int MaxTimeoutCount = 3;
        public const int TimeoutInterval = 3 * 1000;
    }
    public static class UdpFileClient
    {
        public static async Task Sent(UdpTransportClientConfig cfg)
        {
            if (!RecordFile.Check(cfg.SrcFileInfo.FullName))
                return;
            
            using var blockReader = new FileBlockReader(cfg.BlockSize, cfg.SrcFileInfo);
            using var udpClient = new UdpClient();
            using var udpReceived = new UdpClient(cfg.LocalPort);
            var cmd = new CommandPackage();
            var startInfo = new StartCommandInfo
            {
                BlockSize = cfg.BlockSize, TargetFileSize = cfg.SrcFileInfo.Length,
                Version = 1,OverrideMode = cfg.Mode,ClientPort = cfg.LocalPort
            };
            {
                var (buf, count) = PackageBuilder.PrepareStartPack(ref cmd, ref startInfo, cfg.TargetFileName);
                if (!await StartServerAsync(udpClient, buf, count, cfg, udpReceived, cmd.SeqId))
                    return;
            }

            using var cancelSrc = new CancellationTokenSource();
            var token = cancelSrc.Token;
            var verifyIndexMap = new BitArray(blockReader.MaxBlockIndex);
            var fileInfo = cfg.SrcFileInfo;
            using var recorder = new RecordFile(fileInfo.FullName, fileInfo.Length, cfg.BlockSize,
                blockReader.HasherLen, 0);
            var verifySyncObj = new RestartSync();
            var dataSyncObj = new RestartSync();

            var ackTask = ReceiveAckAsync(cfg, verifyIndexMap, udpReceived, cancelSrc, dataSyncObj, verifySyncObj);
            var hashTask = SentHashAsync(cfg, blockReader, verifyIndexMap, recorder, verifySyncObj, startInfo.StartHashIndex, token);
            
            var lastDataTask = Task.CompletedTask;
            var dataInfo = new DataCommandInfo();
            for (var i = startInfo.StartBlockIndex; i < blockReader.MaxBlockIndex; i++)
            {
                if (token.IsCancellationRequested)
                {
                    Logger.Debug("early stop of data blocks transfer");
                    break;
                }
                dataInfo.BlockIndex = i;
                var (buf, count) = PackageBuilder.PrepareDataPack(ref cmd, ref dataInfo, blockReader);
                await lastDataTask;
                lastDataTask = udpClient.SendAsync(buf, count, cfg.TargetAddress);
            }
            //tail task no need to await
            
            await Task.Delay(1, token);
            cmd.Cmd = CommandEnum.DataProgress;
            while (!token.IsCancellationRequested && !dataSyncObj.IsStop())
            {
                {
                    var (buf, count) = PackageBuilder.PrepareAskVerifyProgress(ref cmd);
                    await udpClient.SendAsync(buf, count, cfg.TargetAddress);
                }
                await Task.Delay(1, token);

                var (isStart, startIndex) = dataSyncObj.Read();
                if (!isStart) 
                    continue;
                for (var i = startIndex; i < blockReader.MaxBlockIndex; i++)
                {
                    if (token.IsCancellationRequested || dataSyncObj.IsStop())
                    {
                        Logger.Debug("early stop of data blocks transfer");
                        break;
                    }

                    dataInfo.BlockIndex = i;
                    var (buf, count) = PackageBuilder.PrepareDataPack(ref cmd, ref dataInfo, blockReader);
                    await lastDataTask;
                    lastDataTask = udpClient.SendAsync(buf, count, cfg.TargetAddress);
                }
            }

            await hashTask;
            await ackTask;
            {
                var stopInfo = new StopCommandInfo();
                var (buf, count) = PackageBuilder.PrepareStopPack(ref cmd, ref stopInfo);
                await udpClient.EnsureCmdSent(buf, count, cfg.TargetAddress, cfg.CmdSentCount);
            }

            Logger.Info($"file size: {cfg.SrcFileInfo.Length}");
        }

        private class RestartSync
        {
            private int _prevIndex;
            private int _restartIndex;
            private int _stopTag;

            public void Push(int index)
            {
                if (index > _restartIndex)
                {
                    Interlocked.Exchange(ref _restartIndex, index);
                }
            }

            public (bool,int) Read()
            {
                var tmp = Interlocked.CompareExchange(ref _restartIndex, 0, 0);
                var r = (_prevIndex < tmp, tmp);
                _prevIndex = tmp;
                return r;
            }

            public void Stop()
            {
                Interlocked.CompareExchange(ref _stopTag, 1, 0);
            }

            public bool IsStop()
            {
                var tmp = Interlocked.CompareExchange(ref _stopTag, 1, 1);
                return tmp == 1;
            }
        }

        private static async Task<bool> StartServerAsync(UdpClient udpClient, byte[] sentBuf, int count,
            UdpTransportClientConfig cfg, UdpClient udpReceived, int cmdSeqId)
        {
            await udpClient.EnsureCmdSent(sentBuf, count, cfg.TargetAddress, cfg.CmdSentCount);
            const int maxTimeoutCount = 3;
            const int timeoutInterval = 1 * 1000;
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

                if (ack.Err != StartError.PassCheck)
                {
                    Logger.Warn($"start failed: {ack.Err}");
                    break;
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
            if (timeoutCount >= maxTimeoutCount)
                Logger.Warn($"start command not confirmed: timeout");
            return false;
        }

        private static async Task SentHashAsync(UdpTransportClientConfig cfg, FileBlockReader blockReader,
            BitArray verifyIndexMap, RecordFile recorder, RestartSync verifySyncObj,
            int startHashIndex, CancellationToken token)
        {
            var lastTask = Task.Delay(1, token);
            await lastTask;
            var udpClient = new UdpClient();
            var cmd = new CommandPackage {Cmd = CommandEnum.Verify};
            var vPack = new VerifyCommandInfo();
            for (var i = startHashIndex; i < blockReader.MaxBlockIndex; i++)
            {
                var hashBuf = blockReader.CalculateHash(i);
                if (!verifyIndexMap[i])
                {
                    recorder.SetVerifyCode(hashBuf, i);
                    verifyIndexMap[i] = true;
                }
                //PrepareHashPack not thread safe, make sure the internal buffer is sent
                var (buf, count) = PackageBuilder.PrepareHashPack(ref cmd, ref vPack, hashBuf, i);
                await lastTask;
                lastTask = udpClient.SendAsync(buf, count, cfg.TargetAddress);
            }
            //rely on receiving loop to signal token cancel of timing
            cmd.Cmd = CommandEnum.VerifyProgress;
            while (!token.IsCancellationRequested && !verifySyncObj.IsStop())
            {
                {
                    var (buf, count) = PackageBuilder.PrepareAskVerifyProgress(ref cmd);
                    await udpClient.SendAsync(buf, count, cfg.TargetAddress);
                }
                await Task.Delay(1, token);

                var (isStart, startIndex) = verifySyncObj.Read();
                if (!isStart) 
                    continue;
                for (var i = startIndex; i < blockReader.MaxBlockIndex; i++)
                {
                    if (token.IsCancellationRequested || verifySyncObj.IsStop())
                    {
                        break;
                    }

                    var hashBuf = recorder.GetBlockHash(i);
                    var (buf, count) = PackageBuilder.PrepareHashPack(ref cmd, ref vPack, hashBuf, i);
                    //PrepareHashPack not thread safe, make sure the internal buffer is sent
                    await lastTask;
                    lastTask = udpClient.EnsureCmdSent(buf, count, cfg.TargetAddress, cfg.CmdSentCount);
                }
            }
        }

        private static async Task ReceiveAckAsync(UdpTransportClientConfig cfg, BitArray indexMap,
            UdpClient udpReceived, CancellationTokenSource cancelSrc, RestartSync dataSyncObj, RestartSync verifySyncObj)
        {
            await Task.Delay(1);
            var cmdHeader = new CommandPackage();
            var ack = new AckIndex();
            //限时内数据块或校验值接收无进度就需要停止
            while (!cancelSrc.IsCancellationRequested && (!dataSyncObj.IsStop() || !verifySyncObj.IsStop()))
            {
                var receiveTask = udpReceived.ReceiveAsync();
                if (receiveTask.Timeout(UdpTransportClientConfig.MaxTimeoutCount, UdpTransportClientConfig.TimeoutInterval))
                {
                    Logger.Err($"stop {cfg.SrcFileInfo.Name}, timeout, max count: {UdpTransportClientConfig.MaxTimeoutCount}, timeout interval: {UdpTransportClientConfig.TimeoutInterval}");
                    cancelSrc.Cancel();
                    break;
                }
                var udpResult = await receiveTask;
                if (!udpResult.RemoteEndPoint.Address.Equals(cfg.TargetAddress.Address))
                {
                    Logger.Warn($"unintended client:${udpResult.RemoteEndPoint}");
                    continue;
                }
                var buf = udpResult.Buffer;
                int offset;
                if ((offset = TransportUtil.ExtractCmdHeader(ref cmdHeader, buf)) <= 0)
                    continue;

                switch (cmdHeader.Cmd)
                {
                    case CommandEnum.DataRestart:
                    {
                        if (ack.ReadFrom(buf, offset) == 0)
                        {
                            Logger.Warn("Invalid Data Restart Package");
                            break;
                        }
                        Logger.Debug($"{cmdHeader.Cmd}:{ack.Index}");
                        if (ack.Index < indexMap.Count)
                            dataSyncObj.Push(ack.Index);
                        else if (ack.Index == indexMap.Count) 
                            dataSyncObj.Stop();
                        break;
                    }
                    case CommandEnum.VerifyRestart:
                    {
                        if (ack.ReadFrom(buf, offset) == 0)
                        {
                            Logger.Warn("Invalid Data Restart Package");
                            break;
                        }
                        Logger.Debug($"{cmdHeader.Cmd}:{ack.Index}");
                        if (ack.Index < indexMap.Count)
                            verifySyncObj.Push(ack.Index);
                        else if (ack.Index == indexMap.Count) 
                            verifySyncObj.Stop();
                        break;
                    }
                    case CommandEnum.Stop:
                    {
                        Logger.Warn("Early Stop by Server");
                        cancelSrc.Cancel();
                        break;
                    }
                    default:
                    {
                        Logger.Warn($"Unexpected command: {cmdHeader.Cmd}");
                        break;
                    }
                }
            }
        }
    }
}