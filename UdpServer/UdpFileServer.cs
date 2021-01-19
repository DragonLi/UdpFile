using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

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
        static void Main(string[] args)
        {
            ExtractParams(args, out var listenPort, out var filePrefix);

            var listener = new UdpClient(listenPort);
            var group = new IPEndPoint(IPAddress.Loopback, listenPort);

            var state = ReceiverState.Listening;
            var cmd = new CommandPackage();
            var startCmd = new StartCommandInfo();
            FileBlockDumper? writer = null;
            string targetFileName = string.Empty;
            var dataPack = new DataCommandInfo();
            Logger.Debug($"start udp server, port: {listenPort}, store location: {filePrefix}");
            try
            {
                while (state != ReceiverState.Stop)
                {
                    var bytes = listener.Receive(ref group);
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
                                        break;
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
                                break;
                            }
                            case CommandEnum.Stop:
                            {
                                Logger.Debug($"stop {targetFileName}");
                                state = ReceiverState.Stop;
                                writer?.Dispose();
                                writer = null;
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