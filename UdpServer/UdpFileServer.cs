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
            var taskList = new List<Task>();
            var nextPort = listenPort + 1;
            var targetFileName = string.Empty;
            
            Logger.Debug($"start udp server, port: {listenPort}, store location: {filePrefix}");
            try
            {
                // listener.Connect(filter);
                while (!_serverIsStop)
                {
                    var receiver = listener.ReceiveAsync();
                    while (receiver.Timeout(1, 1000))
                    {//check every second
                        if (_serverIsStop)
                        {
                            Logger.Debug("notice stop signal");
                            break;
                        }
                        //TODO Recycle Task List
                    }

                    if (_serverIsStop)
                    {
                        break;
                    }
                    var udpResult = await receiver;
                    //TODO Logger.Debug($"received from: {udpResult.RemoteEndPoint}");

                    if (!TryDecodeStartCmd(udpResult, ref cmd, ref startCmd, ref targetFileName, filePrefix)) 
                        continue;
                    var port = nextPort;
                    nextPort++;//in case of exception still work
                    var task = ReceiveAsync(port, startCmd, targetFileName, udpResult.RemoteEndPoint);
                    taskList.Add(task);
                }

                Logger.Debug("server is stopping");
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

        private static bool TryDecodeStartCmd(UdpReceiveResult udpResult, ref CommandPackage cmd, 
            ref StartCommandInfo startCmd, ref string targetFileName, string filePrefix)
        {
            var bytes = udpResult.Buffer;
            var offset = 0;
            if (!ExtractCmdHeader(ref cmd, bytes, ref offset)) return false;

            if (cmd.Cmd != CommandEnum.Start)
            {
                Logger.Warn($"expect start command but {cmd.Cmd} received");
                return false;
            }
            targetFileName = startCmd.ReadFrom(bytes, offset);
            if (startCmd.BlockSize <= 0)
            {
                Logger.Debug("invalid BlockSize");
                return false;
            }

            if (startCmd.TargetFileSize <= 0)
            {
                Logger.Debug("invalid TargetFileSize");
                return false;
            }

            if (targetFileName.Length <= 0)
            {
                Logger.Debug("invalid targetFileName");
                return false;
            }

            if (Path.IsPathRooted(targetFileName))
            {
                Logger.Debug($"target file name can not rooted: {targetFileName}");
                return false;
            }
            var targetFsNm = Path.GetFullPath(Path.Combine(filePrefix,targetFileName));
            if (Path.EndsInDirectorySeparator(targetFsNm))
            {
                Logger.Debug($"targetFileName is not a valid file path: {targetFileName}");
                return false;
            }

            if (!targetFsNm.StartsWith(filePrefix))
            {
                Logger.Info($"file name attack: {targetFileName}");
                return false;
            }

            return true;
        }

        private static bool ExtractCmdHeader(ref CommandPackage cmd, byte[] bytes, ref int offset)
        {
            if (bytes.Length <= 0)
            {
                Logger.Debug("invalid package length");
                return false;
            }

            offset = cmd.ReadFrom(bytes, 0);
            if (offset <= 0)
            {
                Logger.Debug("package format error");
                return false;
            }

            return true;
        }

        private static async Task ReceiveAsync(int port, StartCommandInfo startCmd, string targetFileName,
            IPEndPoint udpResultRemoteEndPoint)
        {
            var listener = new UdpClient(port);
            var state = ReceiverState.Listening;
            var cmd = new CommandPackage();
            long fileReceiveCount = 0;
            FileBlockDumper? writer = null;
            var dataPack = new DataCommandInfo();
            var vPack = new VerifyCommandInfo();
            const int maxTimeoutCount = 3;
            const int timeoutInterval = 3 * 1000;
            var seqIdTime = new Dictionary<int, DateTime>();
            var tenMinutes = new TimeSpan(0, 10, 0);

            var udpClient = new UdpClient();
            var cmdSent = new CommandPackage();
            var startAck = new StartAckInfo();
            const int sentCount = 2;
            IPEndPoint clientAddr = null;
            
            if (state != ReceiverState.Listening)
                    {
                        if (receiver.Timeout(maxTimeoutCount, timeoutInterval))
                        {
                            Logger.Err(
                                $"timeout, max count: {maxTimeoutCount}, timeout interval: {timeoutInterval}, received: {fileReceiveCount}");
                            ClearBeforeStop(ref writer, ref clientAddr);
                            break;
                        }
                    }
                    var udpResult = await receiver;

                    var bytes = udpResult.Buffer;
                    if (bytes.Length <= 0)
                    {
                        Logger.Debug("invalid package length");
                        break;
                    }

                    try
                    {
                        var offset = cmd.ReadFrom(bytes, 0);
                        if (offset <= 0)
                        {
                            Logger.Debug("package format error");
                            continue;
                        }
                        switch (cmd.Cmd)
                        {
                            case CommandEnum.Start:
                            {
                                if (writer != null)
                                {
                                    Logger.Info("file transport is already started");
                                    continue;
                                }
                                targetFileName = startCmd.ReadFrom(bytes, offset);
                                if (startCmd.BlockSize <= 0)
                                {
                                    Logger.Debug("invalid BlockSize");
                                    continue;
                                }

                                if (startCmd.TargetFileSize <= 0)
                                {
                                    Logger.Debug("invalid TargetFileSize");
                                    continue;
                                }

                                if (targetFileName.Length <= 0)
                                {
                                    Logger.Debug("invalid targetFileName");
                                    continue;
                                }

                                if (Path.IsPathRooted(targetFileName))
                                {
                                    Logger.Debug($"target file name can not rooted: {targetFileName}");
                                    continue;
                                }
                                var targetFsNm = Path.GetFullPath(Path.Combine(filePrefix,targetFileName));
                                if (Path.EndsInDirectorySeparator(targetFsNm))
                                {
                                    Logger.Debug($"targetFileName is not a valid file path: {targetFileName}");
                                    continue;
                                }

                                if (!targetFsNm.StartsWith(filePrefix))
                                {
                                    Logger.Info($"file name attack: {targetFileName}");
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

                                switch (startCmd.OverrideMode)
                                {
                                    case OverrideModeEnum.NewOrFail:
                                    {
                                        if (File.Exists(targetFsNm))
                                        {
                                            Logger.Info($"target file exists, can not override with mode: {startCmd.OverrideMode}");
                                            continue;
                                        }

                                        Directory.CreateDirectory(Path.GetDirectoryName(targetFsNm));
                                        break;
                                    }
                                    case OverrideModeEnum.Resume:
                                    case OverrideModeEnum.Rename:
                                    {
                                        Logger.Err($"not supported override mode: {startCmd.OverrideMode}");
                                        continue;
                                    }
                                    case OverrideModeEnum.Override:
                                    {
                                        if (File.Exists(targetFsNm) && Directory.Exists(targetFsNm))
                                        {
                                            Logger.Err($"target file is already a directory: {targetFsNm}");
                                            continue;
                                        }
                                        Directory.CreateDirectory(Path.GetDirectoryName(targetFsNm));
                                        break;
                                    }
                                    default:
                                    {
                                        Logger.Debug($"invalid override mode: {startCmd.OverrideMode}");
                                        continue;
                                    }
                                }

                                writer = new FileBlockDumper(targetFsNm, startCmd.BlockSize, startCmd.TargetFileSize);
                                state = ReceiverState.Receiving;
                                clientAddr = udpResult.RemoteEndPoint;
                                clientAddr.Port = startCmd.ClientPort;
                                {
                                    cmdSent.Cmd = CommandEnum.StartAck;
                                    startAck.AckSeqId = cmd.SeqId;
                                    startAck.Port = listenPort;
                                    var (sentBuf, count) = PackageBuilder.PrepareStartAckPack(ref cmdSent, ref startAck, string.Empty);
                                    await udpClient.EnsureCmdSent(sentBuf, count, clientAddr, sentCount);
                                }
                                Logger.Debug($"start transporting: {targetFileName}");
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
                                    ClearBeforeStop(ref writer, ref clientAddr);
                                }
                                var stopInfo = new StopCommandInfo();
                                stopInfo.ReadFrom(bytes, offset);
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Err(e);
                    }
        }

        private static void ClearBeforeStop(ref FileBlockDumper writer, ref IPEndPoint clientAddr)
        {
            writer?.Dispose();
            writer = null;
            clientAddr = null;
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