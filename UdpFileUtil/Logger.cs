using System;

namespace UdpFile
{
    public static class Logger
    {
        public static void Debug(string msg)
        {
            Console.WriteLine(msg);
        }

        public static void Err(Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}