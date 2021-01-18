using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace UdpFile
{
    public enum ReceiverState{
        Listening,
        Receiving,
        Stop,
    }
    
    public static class UdpFileServer
    {
        static void Main(string[] args)
        {
            var listenPort = 9999;
            var listener = new UdpClient(listenPort);
            var group = new IPEndPoint(IPAddress.Loopback, listenPort);
            
            var state = ReceiverState.Listening;
            var cmd = new CommandPackage();
            var startCmd = new StartCommandInfo();
            FileBlockDumper? writer = null;
            string targetFileName = String.Empty;
            var dataPack = new DataCommandInfo();
            try
            {
                while (state != ReceiverState.Stop)
                {
                    var bytes = listener.Receive(ref group);
                    if (bytes.Length <= 0)
                    {
                        continue;
                    }
                    var offset = cmd.ReadFrom(bytes, 0);
                    switch (cmd.Cmd)
                    {
                        case CommandEnum.Start:
                        {
                            targetFileName = startCmd.ReadFrom(bytes, offset);
                            if (startCmd.BlockSize <= 0)
                            {
                                continue;
                            }
                            if (startCmd.TargetFileSize <= 0)
                            {
                                continue;
                            }
                            if (targetFileName.Length <= 0)
                            {
                                continue;
                            }
                            var targetFsNm = Path.GetFullPath(targetFileName);
                            if (Path.EndsInDirectorySeparator(targetFsNm))
                            {
                                continue;
                            }
                            writer = new FileBlockDumper(targetFsNm,startCmd.BlockSize,startCmd.TargetFileSize);
                            state = ReceiverState.Receiving;
                            break;
                        }
                        case CommandEnum.Data:
                        {
                            if (writer == null)
                            {
                                continue;
                            }
                            if (startCmd.BlockSize <= 0)
                            {
                                continue;
                            }
                            offset += dataPack.ReadFrom(bytes,offset);
                            writer.WriteBlock(dataPack.BlockIndex, bytes, bytes.Length - offset);
                            break;
                        }
                        case CommandEnum.Stop:
                        {
                            writer?.Dispose();
                            writer = null;
                            var stopInfo = new StopCommandInfo();
                            stopInfo.ReadFrom(bytes, offset);
                            break;
                        }
                    }
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                listener.Close();
            }
        }
    }
}