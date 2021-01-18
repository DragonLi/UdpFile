using System.Threading;

namespace UdpFile
{
    public static class TransportSeqFactory
    {
        private static int _id = 0;
        public static int NextId()
        {
            return Interlocked.Increment(ref _id);
        }
    }
}