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
        {//TODO randomly control how much to duplicated sent
            for (var i = 0; i < sentCount; i++)
            {
                await udp.SendAsync(buf, count, target);
            }
        }

        public static bool Timeout(this Task receiver, int maxTimeoutCount, int timeoutInterval)
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
        
        public static int ExtractCmdHeader(ref CommandPackage cmd, byte[] bytes)
        {
            if (bytes.Length <= 0)
            {
                Logger.Debug("invalid package length");
                return 0;
            }

            var offset = cmd.ReadFrom(bytes, 0);
            if (offset <= 0)
            {
                Logger.Debug("package format error");
                return 0;
            }
            return offset;
        }
    }
}