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
        static unsafe void StartServer(string[] args)
        {
            var listenPort = 9999;
            var listener = new UdpClient(listenPort);
            var groupEP = new IPEndPoint(IPAddress.Loopback, listenPort);
            var state = ReceiverState.Listening;
            var cmd = new CommandPackage();
            var startCmd = new StartCommandInfo();
            FileBlockDumper writer;
            string targetFileName;
            try
            {
                while (state != ReceiverState.Stop)
                {
                    var bytes = listener.Receive(ref groupEP);
                    var offset = cmd.ReadFrom(bytes, 0);
                    if (cmd.Cmd == CommandEnum.Start)
                    {
                        startCmd.ReadFrom(bytes, offset,out targetFileName, in cmd);
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
                        writer = new FileBlockDumper(targetFileName,startCmd.BlockSize,startCmd.TargetFileSize);
                        state = ReceiverState.Receiving;
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