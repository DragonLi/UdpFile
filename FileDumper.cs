using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace UdpFile
{
    public class FileDumper : IDisposable
    {
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private string _targetFsNm;
        private readonly int _blockSize;
        private long _fileSize;
        private FileStream _fs;

        public FileDumper(string targetFsNm, int bufSize, long fsSize)
        {
            _targetFsNm = targetFsNm;
            _blockSize = bufSize;
            _fileSize = fsSize;
            _fs = File.Create(targetFsNm);
            _mmf=MemoryMappedFile.CreateFromFile(_fs,null,fsSize,MemoryMappedFileAccess.ReadWrite,HandleInheritability.None,false);
            _accessor = _mmf.CreateViewAccessor();
        }

        public void WriteBlock(int blockIndex, byte[] buf, int count)
        {
            _accessor.WriteArray(blockIndex * _blockSize, buf, 0, count);
        }

        public void Dispose()
        {
            _mmf.Dispose();
            _accessor.Dispose();
            _fs.Dispose();
            Console.Out.WriteLine("file dump finished");
        }
    }
}