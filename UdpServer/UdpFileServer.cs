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
        Listening,
        Receiving,
        Stop,
    }
    
    public class UdpServerConfig
    {
        public int listenPort;
        public string filePrefix;
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
            var listenPort = cfg.listenPort;
            var filePrefix = cfg.filePrefix;
            var listener = new UdpClient(listenPort);
            //TODO var filter = new IPEndPoint(IPAddress.Any, listenPort);
            
            var cmd = new CommandPackage();
            var startCmd = new StartCommandInfo();
            var seqIdTime = new Dictionary<int, DateTime>();
            var tenMinutes = new TimeSpan(0, 10, 0);
            var nextPort = listenPort + 1;
            var clientList = new Dictionary<Task, IPAddress>();
            

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

                        var tmp = new List<Task>(clientList.Count);
                        foreach (var clientTask in clientList.Keys)
                        {
                            if (clientTask.IsCompleted)
                            {
                                tmp.Add(clientTask);
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

                    string targetFileName;
                    if ((targetFileName = TryDecodeStartCmd(udpResult, ref cmd, ref startCmd, filePrefix, seqIdTime,
                        tenMinutes)).Equals(string.Empty))
                        continue;
                    var clientAddr = udpResult.RemoteEndPoint.Address;
                    if (clientList.ContainsValue(clientAddr))
                    {
                        Logger.Warn($"client {clientAddr} is already started");
                        continue;
                    }
                    var port = nextPort;
                    nextPort++;//in case of exception still work
                    var task = ReceiveAsync(port, startCmd, targetFileName, udpResult.RemoteEndPoint, cmd.SeqId);
                    clientList.Add(task,clientAddr);
                }

                Logger.Debug("server is stopping");
                var taskList = new Task[clientList.Count];
                clientList.Keys.CopyTo(taskList, 0);
                await Task.WhenAll(taskList);
            }
            catch (SocketException e)
            {
                Logger.Err(e);
            }
            finally
            {
                listener.Close();
            }
        }

        private static string TryDecodeStartCmd(UdpReceiveResult udpResult, ref CommandPackage cmd, 
            ref StartCommandInfo startCmd, string filePrefix, Dictionary<int, DateTime> seqIdTime,in TimeSpan expiredAdd)
        {
            var bytes = udpResult.Buffer;
            int offset;
            if ((offset = ExtractCmdHeader(ref cmd, bytes, seqIdTime, expiredAdd)) <= 0)
                return string.Empty;

            if (cmd.Cmd != CommandEnum.Start)
            {
                Logger.Warn($"expect start command but {cmd.Cmd} received");
                return string.Empty;
            }
            var targetFileName = startCmd.ReadFrom(bytes, offset);
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
            var targetFsNm = Path.GetFullPath(Path.Combine(filePrefix,targetFileName));
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

            return targetFileName;
        }

        private static int ExtractCmdHeader(ref CommandPackage cmd, byte[] bytes,Dictionary<int, DateTime> seqIdTime,in TimeSpan expiredAdd)
        {
            if (bytes.Length <= 0)
            {
                Logger.Debug("invalid package length");
                return 0;
            }

            var offset = cmd.ReadFrom(bytes, 0);
            if (offset <= 0)
            {
                Logger.Debug("package format error");
                return 0;
            }
            var now = DateTime.Now;
            if (seqIdTime.TryGetValue(cmd.SeqId, out var expiredTime) && now < expiredTime)
            {
                Logger.Debug("duplicate command received");
                return 0;
            }
            else
            {
                seqIdTime[cmd.SeqId] = now + expiredAdd;
            }
            return offset;
        }

        private static async Task ReceiveAsync(int port, StartCommandInfo startCmd, string targetFileName,
            IPEndPoint clientIp,int startSeqId)
        {
            if (TargetFilePathIsNotValid(in startCmd, targetFileName)) 
                return;
            var writer = new FileBlockDumper(targetFileName, startCmd.BlockSize, startCmd.TargetFileSize);
            var udpClient = new UdpClient();
            var cmdSent = new CommandPackage();
            var startAck = new StartAckInfo();
            const int sentCount = 2;
            var clientAddr = clientIp;
            clientAddr.Port = startCmd.ClientPort;
            {
                cmdSent.Cmd = CommandEnum.StartAck;
                startAck.AckSeqId = cmd.SeqId;
                startAck.Port = port;
                var (sentBuf, count) =
                    PackageBuilder.PrepareStartAckPack(ref cmdSent, ref startAck, string.Empty);
                await udpClient.EnsureCmdSent(sentBuf, count, clientAddr, sentCount);
            }
            Logger.Debug($"start transporting: {targetFileName}");

            var listener = new UdpClient(port);
            var cmd = new CommandPackage();
            long fileReceiveCount = 0;
            var dataPack = new DataCommandInfo();
            var vPack = new VerifyCommandInfo();
            const int maxTimeoutCount = 3;
            const int timeoutInterval = 3 * 1000;
            var seqIdTime = new Dictionary<int, DateTime>();
            var expiredAdd = new TimeSpan(0, 10, 0);
            var state = ReceiverState.Receiving;


            while (state != ReceiverState.Stop)
            {
                var receiver = listener.ReceiveAsync();
                if (receiver.Timeout(maxTimeoutCount, timeoutInterval))
                {
                    Logger.Err(
                        $"timeout, max count: {maxTimeoutCount}, timeout interval: {timeoutInterval}, received: {fileReceiveCount}");
                    ClearBeforeStop(ref writer);
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
                if ((offset = ExtractCmdHeader(ref cmd, bytes, seqIdTime, expiredAdd)) <= 0)
                    continue;

                    switch (cmd.Cmd)
                    {
                        case CommandEnum.Start:
                        {
                            break;
                        }
                        case CommandEnum.Data:
                        {
                            if (state != ReceiverState.Receiving)
                            {
                                Logger.Debug("incorrect sequence: data package before start command");
                                continue;
                            }

                            if (startCmd.BlockSize <= 0)
                            {
                                Logger.Debug("invalid BlockSize");
                                continue;
                            }

                            offset += dataPack.ReadFrom(bytes, offset);
                            writer.WriteBlock(dataPack.BlockIndex, bytes, bytes.Length - offset, offset);
                            fileReceiveCount += bytes.Length - offset;
                            break;
                        }
                        case CommandEnum.Verify:
                        {
                            if (state != ReceiverState.Receiving)
                            {
                                Logger.Debug("incorrect sequence: file transport not started");
                                continue;
                            }

                            //TODO refactor: extract as function
                            var now = DateTime.Now;
                            if (seqIdTime.TryGetValue(cmd.SeqId, out var expiredTime) && now < expiredTime)
                            {
                                Logger.Debug("duplicate command received");
                                continue;
                            }
                            else
                            {
                                seqIdTime[cmd.SeqId] = now + tenMinutes;
                            }

                            var vBuf = vPack.ReadFrom(bytes, offset);
                            VerifyAsync(writer, vBuf, vPack.BlockIndex);
                            break;
                        }
                        case CommandEnum.Stop:
                        {
                            if (state != ReceiverState.Receiving)
                            {
                                Logger.Debug("incorrect sequence: file transport not started");
                                continue;
                            }

                            if (fileReceiveCount < startCmd.TargetFileSize)
                            {
                                Logger.Debug("stopping, waiting all packages");
                            }
                            else
                            {
                                Logger.Debug($"stop {targetFileName},received: {fileReceiveCount}");
                                state = ReceiverState.Stop;
                                ClearBeforeStop(ref writer);
                            }

                            var stopInfo = new StopCommandInfo();
                            stopInfo.ReadFrom(bytes, offset);
                            break;
                        }
                    }
                

            }

            try
            {

            }
            catch (Exception e)
            {
                Logger.Err(e);
            }finally
            {
                listener.Close();
            }
        }

        private static bool TargetFilePathIsNotValid(in StartCommandInfo startCmd, string targetFileName)
        {
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

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFileName));
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

                    Directory.CreateDirectory(Path.GetDirectoryName(targetFileName));
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

        private static async Task VerifyAsync(FileBlockDumper writer, byte[] vBuf, long blockIndex)
        {
            var isOk = writer.Verify(blockIndex, vBuf);
            if (!isOk)
            {
                Logger.Debug($"{blockIndex} not verify");
            }
            //TODO
        }

    }
}