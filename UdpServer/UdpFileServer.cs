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

    public static class UdpFileServer
    {
        static async Task Main(string[] args)
        {
            ExtractParams(args, out var listenPort, out var filePrefix);

            var listener = new UdpClient(listenPort);
            var filter = new IPEndPoint(IPAddress.Any, listenPort);

            var state = ReceiverState.Listening;
            var cmd = new CommandPackage();
            var startCmd = new StartCommandInfo();
            long fileReceiveCount = 0;
            FileBlockDumper? writer = null;
            string targetFileName = string.Empty;
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
            
            Logger.Debug($"start udp server, port: {listenPort}, store location: {filePrefix}");
            try
            {
                // listener.Connect(filter);
                while (state != ReceiverState.Stop)
                {
                    var receiver = listener.ReceiveAsync();
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
                    // Logger.Debug($"received from: {udpResult.RemoteEndPoint}");
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

        private static void ExtractParams(string[] args, out int listenPort, out string filePrefix)
        {
            if (args.Length <= 0 || !int.TryParse(args[0], out listenPort))
            {
                listenPort = 9999;
            }

            if (args.Length > 1)
            {
                filePrefix = args[1];
                var t = new DirectoryInfo(filePrefix);
                if (!t.Exists)
                {
                    Logger.Err(
                        $"invalid store location: {filePrefix}, use current directory instead: {Environment.CurrentDirectory}");
                    filePrefix = Environment.CurrentDirectory;
                }
            }
            else
                filePrefix = Environment.CurrentDirectory;
        }
    }
}