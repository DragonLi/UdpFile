using System;
using System.IO;

namespace UdpFile
{
    public class RecordFile : IDisposable
    {
        public static bool Check(string fullPath)
        {
            var partFileName = fullPath + ".parts";
            if (File.Exists(partFileName) && Directory.Exists(partFileName))
            {
                Logger.Err($"{partFileName} is a directory!");
                return false;
            }

            return true;
        }

        public RecordFile(string fullPath, long fileSize, int blockSize, int hasherSize, int reservedByteCount)
        {
            var partFileName = fullPath + ".parts";
        }

        public void SetVerifyCode(byte[] verification, int blockIndex)
        {
            
        }

        public void Dispose()
        {
        }

        public byte[] GetBlockHash(int blockIndex)
        {
            
        }
    }
}