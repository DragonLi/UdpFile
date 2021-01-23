using System;
using System.Threading;

namespace UdpFile
{
    public static class TransportSeqFactory
    {
        private static int _id = (int) DateTime.Now.Ticks;
        public static int NextId()
        {
            return Interlocked.Increment(ref _id);
        }
    }
}