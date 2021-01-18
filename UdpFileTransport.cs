using System;
using System.IO;
using System.Net;

namespace UdpFile
{
    class Program
    {
        static void Main(string[] args)
        {
            var fsNm = GetArgs(args,0);
            if (!File.Exists(fsNm))
            {
                Console.Out.WriteLine($"File not exists: {fsNm}");
                return;
            }

            var ip = GetArgs(args,1);
            if (!IPAddress.TryParse(ip, out var targetAddress))
            {
                Console.Out.WriteLine($"Ip not valid: {ip}");
                return;
            }

            var targetFsNm = GetArgs(args,2);
            try
            {
                targetFsNm = Path.GetFullPath(targetFsNm);
                if (Path.EndsInDirectorySeparator(targetFsNm))
                {
                    Console.Out.WriteLine($"target file name must not be a directory: {GetArgs(args,2)}");
                    return;
                }
            }
            catch (Exception e)
            {
                Console.Out.WriteLine($"Invalid target file name: {GetArgs(args,2)}");
                Console.WriteLine(e);
                return;
            }

            Console.Out.WriteLine($"{fsNm},{ip},{targetFsNm}");
            
            UdpFileTransportController.Sent(new FileInfo(fsNm),targetAddress,targetFsNm);
            
            Console.Out.WriteLine("finished");
            //diff -s /tmp/UdpFileTransport.dll /tmp/UdpFile/bin/Debug/net5.0/UdpFileTransport.dll
            
        }

        private static string GetArgs(string[] args, int i) => i < args.Length ? args[i] : string.Empty;
    }
}