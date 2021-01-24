using System;
using System.Runtime.CompilerServices;

namespace UdpFile
{
    public static partial class Logger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string msg)
        {
            Debug1(msg);
        }
        static partial void Debug1(string msg);
        #if DEBUG
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static partial void Debug1(string msg)
        {
            Console.Write("Debug: ");
            Console.WriteLine(msg);
        }
        #endif

        public static void Warn(string msg)
        {
            Console.Write("Warn: ");
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