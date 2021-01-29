using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UdpFile
{
    public enum ReceiverState
    {
        //Listening,
        Receiving,
        Stop,
    }
    
    public class UdpServerConfig
    {
        public int ListenPort;
        public string StoreLocation;
        public TimeSpan ExpiredAdd;
        public const int MaxTimeoutCount = 3;
        public const int TimeoutInterval = 3 * 1000;
    }

    public static class UdpFileServer
    {
        private static volatile bool _serverIsStop;

        public static void StopServer()
        {
            if (!_serverIsStop)
                _serverIsStop = true;
        }
        
        public static async Task Start(UdpServerConfig cfg)
        {
            await Task.Delay(1);
            var listenPort = cfg.ListenPort;
            var filePrefix = cfg.StoreLocation;
            using var listener = new UdpClient(listenPort);
            //TODO var filter = new IPEndPoint(IPAddress.Any, listenPort);
            
            var cmd = new CommandPackage();
            var startCmd = new StartCommandInfo();
            var seqIdTime = new Dictionary<int, DateTime>();
            var tenMinutes = cfg.ExpiredAdd;
            var nextPort = listenPort + 1;
            var clientList = new Dictionary<IPEndPoint,Task>();
            

            Logger.Debug($"start udp server, port: {listenPort}, store location: {filePrefix}");
            try
            {
                // listener.Connect(filter);
                while (!_serverIsStop)
                {
                    var receiver = listener.ReceiveAsync();
                    while (receiver.Timeout(1, 1000))
                    {
                        //check every second
                        if (_serverIsStop)
                        {
                            Logger.Debug("notice stop signal");
                            break;
                        }

                        var tmp = new List<IPEndPoint>(clientList.Count);
                        foreach (var (ip,clientTask) in clientList)
                        {
                            if (clientTask.IsCompleted)
                            {
                                tmp.Add(ip);
                            }
                        }
                        foreach (var k in tmp)
                        {
                            clientList.Remove(k);
                        }
                    }

                    if (_serverIsStop)
                    {
                        break;
                    }
                    var udpResult = await receiver;
                    //TODO Black List udpResult.RemoteEndPoint

                    var targetFileName = ExtractStartCmdInfo(udpResult, ref cmd, ref startCmd, filePrefix, seqIdTime,
                        tenMinutes);
                    if (targetFileName.Equals(string.Empty))
                        continue;
                    var clientAddr = udpResult.RemoteEndPoint;
                    if (clientList.TryGetValue(clientAddr,out var oldTask) && !oldTask.IsCompleted)
                    {
                        Logger.Warn($"client {clientAddr} is already started");
                        continue;
                    }
                    var port = nextPort;
                    nextPort++;//in case of exception still work
                    var task = ReceiveAsync(port, startCmd, targetFileName, udpResult.RemoteEndPoint, cmd.SeqId, tenMinutes);
                    clientList.Add(clientAddr,task);
                    Logger.Debug($"client added: {clientAddr}");
                }

                Logger.Debug("server is stopping");
                var taskList = new Task[clientList.Count];
                clientList.Values.CopyTo(taskList, 0);
                await Task.WhenAll(taskList);
            }
            catch (SocketException e)
            {
                Logger.Err(e);
            }
        }

        private static string ExtractStartCmdInfo(UdpReceiveResult udpResult, ref CommandPackage cmd, 
            ref StartCommandInfo startCmd, string filePrefix, Dictionary<int, DateTime> seqIdTime,in TimeSpan expiredAdd)
        {
            var bytes = udpResult.Buffer;
            int offset;
            if ((offset = TransportUtil.ExtractCmdHeader(ref cmd, bytes)) <= 0)
                return string.Empty;

            if (cmd.Cmd != CommandEnum.Start)
            {
                Logger.Warn($"expect start command but {cmd.Cmd} received");
                return string.Empty;
            }
            
            var targetFileName = startCmd.ReadFrom(bytes, offset);
            if (startCmd.Version != 1)
            {
                Logger.Debug($"incompatible version {startCmd.Version}");
                return string.Empty;
            }
            if (startCmd.BlockSize <= 0)
            {
                Logger.Debug("invalid BlockSize");
                return string.Empty;
            }

            if (startCmd.TargetFileSize <= 0)
            {
                Logger.Debug("invalid TargetFileSize");
                return string.Empty;
            }

            if (targetFileName.Length <= 0)
            {
                Logger.Debug("invalid targetFileName");
                return string.Empty;
            }

            if (Path.IsPathRooted(targetFileName))
            {
                Logger.Debug($"target file name can not rooted: {targetFileName}");
                return string.Empty;
            }

            var targetFsNm = Path.GetFullPath(Path.Combine(filePrefix, targetFileName));
            if (Path.EndsInDirectorySeparator(targetFsNm))
            {
                Logger.Debug($"targetFileName is not a valid file path: {targetFileName}");
                return string.Empty;
            }

            if (!targetFsNm.StartsWith(filePrefix))
            {
                Logger.Info($"file name attack: {targetFileName}");
                return string.Empty;
            }
            
            if (CheckIsDuplicatedPackage(cmd.SeqId, seqIdTime, expiredAdd)) 
                return string.Empty;

            return targetFsNm;
        }

        private static bool CheckIsDuplicatedPackage(int cmdSeq, Dictionary<int, DateTime> seqIdTime, in TimeSpan expiredAdd)
        {
            var now = DateTime.Now;
            if (seqIdTime.TryGetValue(cmdSeq, out var expiredTime) && now < expiredTime)
            {
                Logger.Debug("duplicate command received");
                return true;
            }

            seqIdTime[cmdSeq] = now + expiredAdd;
            return false;
        }
        
        private static async Task ReceiveAsync(int port, StartCommandInfo startCmd, string targetFileName,
            IPEndPoint clientIp, int startSeqId, TimeSpan expiredAdd)
        {
            if (TargetFilePathIsNotValid(in startCmd, targetFileName)) 
                return;
            
            using var udpClient = new UdpClient();
            var cmdSent = new CommandPackage();
            const int sentCount = 2;
            var clientAddr = clientIp;
            clientAddr.Port = startCmd.ClientPort;
            cmdSent.Cmd = CommandEnum.StartAck;
            //bind port before sent ack!
            using var listener = new UdpClient(port);
            {//TODO startAck shall be prepare by main thread in ExtractStartCmdInfo as a response
                var startAck = new StartAckInfo {AckSeqId = startSeqId, Port = port};
                var (sentBuf, count) =
                    PackageBuilder.PrepareStartAckPack(ref cmdSent, ref startAck, string.Empty);
                await udpClient.EnsureCmdSent(sentBuf, count, clientAddr, sentCount);
            }

            var cmd = new CommandPackage();
            long fileReceiveCount = 0;
            var dataPack = new DataCommandInfo();
            var vPack = new VerifyCommandInfo();
            var seqIdTime = new Dictionary<int, DateTime>();
            var state = ReceiverState.Receiving;
            using var progressRecorder = new ReceiveProgress(startCmd.TargetFileSize, startCmd.BlockSize);
            using var writer = new FileBlockDumper(targetFileName, startCmd.BlockSize, startCmd.TargetFileSize);
            using var recorder = new RecordFile(targetFileName, startCmd.TargetFileSize, startCmd.BlockSize,
                writer.HasherCodeSize, progressRecorder.Size);
            await Task.Delay(1);
            Logger.Debug($"start transporting: {targetFileName}, listen port: {port}");

            try
            {
                while (state != ReceiverState.Stop)
                {
                    var receiver = listener.ReceiveAsync();
                    if (receiver.Timeout(UdpServerConfig.MaxTimeoutCount, UdpServerConfig.TimeoutInterval))
                    {
                        Logger.Err(
                            $"stop {targetFileName}, timeout, max count: {UdpServerConfig.MaxTimeoutCount}, timeout interval: {UdpServerConfig.TimeoutInterval}, received: {fileReceiveCount}");
                        break;
                    }

                    var udpResult = await receiver;
                    if (!udpResult.RemoteEndPoint.Address.Equals(clientIp.Address))
                    {
                        Logger.Warn($"unintended client:${udpResult.RemoteEndPoint}");
                        continue;
                    }
                    var bytes = udpResult.Buffer;
                    int offset;
                    if ((offset = TransportUtil.ExtractCmdHeader(ref cmd, bytes)) <= 0)
                        continue;

                    switch (cmd.Cmd)
                    {
                        case CommandEnum.Data:
                        {
                            var readCount = dataPack.ReadFrom(bytes, offset);
                            offset += readCount;
                            var blockLen = bytes.Length - offset;
                            var blockIndex = dataPack.BlockIndex;
                            if (readCount <=0 || blockIndex< 0 || blockLen <= 0)
                            {
                                continue;
                            }
                            writer.WriteBlock(blockIndex, bytes, blockLen, offset);
                            fileReceiveCount += blockLen;
                            progressRecorder.NotifyBlock(blockIndex, writer, recorder);
                            break;
                        }
                        case CommandEnum.Verify:
                        {
                            var vBuf = vPack.ReadFrom(bytes, offset);
                            if (vBuf.Length <= 0 || vPack.BlockIndex < 0 ||
                                CheckIsDuplicatedPackage(cmd.SeqId, seqIdTime, expiredAdd))
                            {
                                continue;
                            }

                            VerifyAsync(writer, vBuf, vPack.BlockIndex, recorder, progressRecorder);
                            break;
                        }
                        case CommandEnum.Stop:
                        {
                            if (fileReceiveCount < startCmd.TargetFileSize)
                            {
                                Logger.Debug("stopping, waiting all packages");
                            }
                            else
                            {
                                Logger.Debug($"stop {targetFileName},received: {fileReceiveCount}");
                                state = ReceiverState.Stop;
                            }

                            var stopInfo = new StopCommandInfo();
                            stopInfo.ReadFrom(bytes, offset);
                            break;
                        }
                        default:
                        {
                            Logger.Warn($"unexpected command: {cmd.Cmd}");
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Err(e);
            }
            Logger.Debug($"stop listen {port}");
        }

        private static bool TargetFilePathIsNotValid(in StartCommandInfo startCmd, string targetFileName)
        {
            var dir = Path.GetDirectoryName(targetFileName);
            switch (startCmd.OverrideMode)
            {
                case OverrideModeEnum.NewOrFail:
                {
                    if (File.Exists(targetFileName))
                    {
                        Logger.Info(
                            $"target file exists, can not override with mode: {startCmd.OverrideMode}");
                        return true;
                    }
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    break;
                }
                case OverrideModeEnum.Resume:
                case OverrideModeEnum.Rename:
                {
                    Logger.Err($"not supported override mode: {startCmd.OverrideMode}");
                    return true;
                }
                case OverrideModeEnum.Override:
                {
                    if (File.Exists(targetFileName) && Directory.Exists(targetFileName))
                    {
                        Logger.Err($"target file is already a directory: {targetFileName}");
                        return true;
                    }

                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    break;
                }
                default:
                {
                    Logger.Debug($"invalid override mode: {startCmd.OverrideMode}");
                    return true;
                }
            }

            return false;
        }

        private static void ClearBeforeStop(ref FileBlockDumper writer)
        {
            writer?.Dispose();
            writer = null;
        }

        private static async Task VerifyAsync(FileBlockDumper writer, byte[] vBuf, int blockIndex,
            RecordFile recorder, ReceiveProgress progressRecorder)
        {
            progressRecorder.TryVerifyCode(vBuf, blockIndex, writer, recorder);
            var isOk = writer.Verify(blockIndex, vBuf);
            if (!isOk)
            {
                Logger.Debug($"{blockIndex} not verify");
            }
            //TODO
        }

    }
}