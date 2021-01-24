using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;

namespace UdpFile
{
    public class FileBlockDumper : IDisposable
    {
        private string _targetFsNm;
        private long _fileSize;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly int _blockSize;
        private readonly FileStream _fs;
        private readonly long _max;
        private MemoryMappedViewAccessor _hashReader;
        private SHA512Managed _hasher;
        private readonly byte[] _hashBuf;

        public FileBlockDumper(string targetFsNm, int blockSize, long fileSize)
        {
            _targetFsNm = targetFsNm;
            _blockSize = blockSize;
            _fileSize = fileSize;
            _hasher = new SHA512Managed();
            _fs = File.Create(targetFsNm);
            _mmf = MemoryMappedFile.CreateFromFile(_fs, null, fileSize, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, false);
            _accessor = _mmf.CreateViewAccessor();
            _hashReader = _mmf.CreateViewAccessor();
            var max = fileSize / blockSize;
            _max = fileSize - max * blockSize > 0 ? max + 1 : max;
            _hashBuf = new byte[blockSize];
        }
        
        public long MaxBlockIndex => _max;

        public void WriteBlock(long blockIndex, byte[] buf, int count,int offset = 0)
        {
            if (count <= 0 || blockIndex < 0 || blockIndex >= _max)
                return;
            Console.WriteLine($"write {blockIndex}");
            _accessor.WriteArray(blockIndex * _blockSize, buf, offset, count);
        }

        public bool Verify(long blockIndex, byte[] vBuf)
        {
            if (blockIndex < 0 || blockIndex >= _max || vBuf.Length != _hasher.HashSize/8)
                return false;
            Console.WriteLine($"verify {blockIndex}");
            var readCount = _hashReader.ReadArray(blockIndex * _blockSize, _hashBuf, 0, _blockSize);
            var hash = _hasher.ComputeHash(_hashBuf, 0, readCount);
            for (var i = 0; i < vBuf.Length; i++)
            {
                if (hash[i] != vBuf[i])
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            _mmf.Dispose();
            _accessor.Dispose();
            _fs.Dispose();
        }
    }
}