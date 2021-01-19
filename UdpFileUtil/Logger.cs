using System;

namespace UdpFile
{
    public static class Logger
    {
        public static void Debug(string msg)
        {
            Console.Write("Debug: ");
            Console.WriteLine(msg);
        }

        public static void Err(Exception e)
        {
            Console.Write("Error: ");
            Console.WriteLine(e.Message);
        }

        public static void Err(string msg)
        {
            Console.Write("Error: ");
            Console.WriteLine(msg);
        }

        public static void Info(string msg)
        {
            Console.Write("Info: ");
            Console.WriteLine(msg);
        }
    }
}