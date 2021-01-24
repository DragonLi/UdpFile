using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace UdpFile
{
    public static class TransportUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static async Task EnsureCmdSent(this UdpClient udp, byte[] buf, int count, IPEndPoint target,
            int sentCount)
        {
            for (var i = 0; i < sentCount; i++)
            {
                await udp.SendAsync(buf, count, target);
            }
        }

        public static bool Timeout(this Task<UdpReceiveResult> receiver, int maxTimeoutCount, int timeoutInterval)
        {
            var timeOutSignal = -1;
            var timeoutCount = 0;
            while (timeOutSignal == -1 && timeoutCount < maxTimeoutCount)
            {
                timeOutSignal = Task.WaitAny(new Task[] {receiver}, timeoutInterval);
                if (timeOutSignal < 0)
                {
                    timeoutCount++;
                }
            }

            return timeOutSignal < 0;
        }
    }
}