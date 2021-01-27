using System;

namespace UdpFile
{
    public class ReceiveProgress : IDisposable
    {
        private int _size;

        public ReceiveProgress(long fileSize, int blockSize)
        {
            
        }

        public void NotifyBlock(int blockIndex, FileBlockDumper writer, RecordFile recorder)
        {
        }

        public void TryVerifyCode(byte[] buf, int blockIndex, FileBlockDumper writer, RecordFile recorder)
        {
        }

        public void Dispose()
        {
            
        }

        public int Size => _size;
    }
}