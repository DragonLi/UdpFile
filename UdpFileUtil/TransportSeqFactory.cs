using System;
using System.Threading;

namespace UdpFile
{
    public static class TransportSeqFactory
    {
        private static int _id = new Random((int)DateTime.Now.Ticks).Next();
        public static int NextId()
        {
            return Interlocked.Increment(ref _id);
        }
    }
}