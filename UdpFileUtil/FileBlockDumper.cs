using System;
using System.IO;
using System.IO.MemoryMappedFiles;

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

        public FileBlockDumper(string targetFsNm, int bufSize, long fsSize)
        {
            _targetFsNm = targetFsNm;
            _blockSize = bufSize;
            _fileSize = fsSize;
            _fs = File.Create(targetFsNm);
            _mmf=MemoryMappedFile.CreateFromFile(_fs,null,fsSize,MemoryMappedFileAccess.ReadWrite,HandleInheritability.None,false);
            _accessor = _mmf.CreateViewAccessor();
        }

        public void WriteBlock(long blockIndex, byte[] buf, int count,int offset = 0)
        {
            if (count <= 0)
                return;
            _accessor.WriteArray(blockIndex * _blockSize, buf, offset, count);
        }

        public void Dispose()
        {
            _mmf.Dispose();
            _accessor.Dispose();
            _fs.Dispose();
        }
    }
}