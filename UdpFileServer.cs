using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UdpFile
{
    public static class UdpFileServer
    {
        static void StartServer(string[] args)
        {
            var listenPort = 9999;
            var listener = new UdpClient(listenPort);
            var groupEP = new IPEndPoint(IPAddress.Any, listenPort);

            try
            {
                while (true)
                {
                    Console.WriteLine("Waiting for broadcast");
                    var bytes = listener.Receive(ref groupEP);

                    Console.WriteLine($"Received broadcast from {groupEP} :");
                    Console.WriteLine($" {Encoding.ASCII.GetString(bytes, 0, bytes.Length)}");
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