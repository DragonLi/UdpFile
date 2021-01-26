using System;
using System.IO;
using System.Threading.Tasks;

namespace UdpFile
{
    internal static class ConsoleDriverServer
    {
        private static async Task Main(string[] args)
        {
            var cfg = ExtractParams(args);
            var msg = string.Empty;
            var task = UdpFileServer.Start(cfg);
            do
            {
                var read = Console.ReadLine();
                if (read != null)
                    msg = read;
            } while (msg.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase));
            UdpFileServer.StopServer();
            await task;
        }
        
        private static UdpServerConfig ExtractParams(string[] args)
        {
            UdpServerConfig cfg = new();
            if (args.Length <= 0 || !int.TryParse(args[0], out var listenPort))
            {
                listenPort = 9999;
            }

            cfg.ListenPort = listenPort;
            string filePrefix;
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

            cfg.StoreLocation = filePrefix;
            cfg.ExpiredAdd = new TimeSpan(0, 10, 0);
            return cfg;
        }
    }
}