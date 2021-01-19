using System;
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
            const int maxTimeoutCount = 3;
            const int timeoutInterval = 3 * 1000;
            var timeoutCount = 0;
            Logger.Debug($"start udp server, port: {listenPort}, store location: {filePrefix}");
            try
            {
                // listener.Connect(filter);
                while (state != ReceiverState.Stop)
                {
                    var receiver = listener.ReceiveAsync();
                    var timeOutSignal = -1;
                    if (state != ReceiverState.Listening)
                    {
                        while (timeOutSignal ==-1 && timeoutCount < maxTimeoutCount)
                        {
                            timeOutSignal = Task.WaitAny(new Task[]{receiver}, timeoutInterval);
                            if (timeOutSignal < 0)
                            {
                                timeoutCount++;
                            }
                        }

                        if (timeOutSignal >= 0)
                        {
                            timeoutCount = 0;
                        }
                        else
                        {
                            Logger.Err($"timeout, max count: {maxTimeoutCount}, timeout interval: {timeoutInterval}, received: {fileReceiveCount}");
                            writer?.Dispose();
                            writer = null;
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

                                switch (startCmd.OverriteMode)
                                {
                                    case OverrideModeEnum.NewOrFail:
                                    {
                                        if (File.Exists(targetFsNm))
                                        {
                                            Logger.Info($"target file exists, can not override with mode: {startCmd.OverriteMode}");
                                            continue;
                                        }

                                        Directory.CreateDirectory(Path.GetDirectoryName(targetFsNm));
                                        break;
                                    }
                                    case OverrideModeEnum.Resume:
                                    case OverrideModeEnum.Rename:
                                    {
                                        Logger.Err($"not supported override mode: {startCmd.OverriteMode}");
                                        continue;
                                    }
                                    case OverrideModeEnum.Override:
                                    {
                                        if (File.Exists(targetFsNm) && Directory.Exists(targetFsNm))
                                        {
                                            Logger.Err($"target file is already exist as a directory: {targetFsNm}");
                                            continue;
                                        }
                                        Directory.CreateDirectory(Path.GetDirectoryName(targetFsNm));
                                        break;
                                    }
                                    default:
                                    {
                                        Logger.Debug($"invalid override mode: {startCmd.OverriteMode}");
                                        continue;
                                    }
                                }

                                writer = new FileBlockDumper(targetFsNm, startCmd.BlockSize, startCmd.TargetFileSize);
                                state = ReceiverState.Receiving;
                                Logger.Debug($"start transporting: {targetFileName}");
                                break;
                            }
                            case CommandEnum.Data:
                            {
                                if (writer == null)
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
                                    writer?.Dispose();
                                    writer = null;
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