using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace UdpFile
{
    public class FileBlockReader : IDisposable, IEnumerable<(byte[],int, long)>
    {
        private readonly byte[] _buf;
        private readonly int _blockSize;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly FileInfo _srcFile;
        private readonly long _max;
        private readonly int _headerSize;
        private readonly int _cmdSize;

        public FileBlockReader(int blockSize, FileInfo srcFile)
        {
            _blockSize = blockSize;
            _srcFile = srcFile;
            _mmf = MemoryMappedFile.CreateFromFile(srcFile.FullName);
            _accessor = _mmf.CreateViewAccessor();
            unsafe
            {
                _cmdSize = sizeof(CommandPackage);
                _headerSize = _cmdSize + sizeof(DataCommandInfo);
            }
            _buf = new byte[_headerSize+blockSize];
            var total = srcFile.Length;
            var max = total / blockSize;
            _max = total - max * blockSize > 0 ? max + 1 : max;
        }

        public long MaxBlockIndex => _max;

        public (byte[], int) QuickPrepare(ref CommandPackage cmd, ref DataCommandInfo info)
        {
            var blockIndex = info.BlockIndex;
            var readCount = _accessor.ReadArray(blockIndex * _blockSize, _buf, _headerSize, _blockSize);
            cmd.Cmd = CommandEnum.Data;
            cmd.WriteTo(_buf, 0);
            info.WriteTo(_buf, _cmdSize);
            return (_buf, readCount + _headerSize);
        }

        public (byte[], int) Read(long blockIndex)
        {
            if (0 > blockIndex || blockIndex >= _max)
            {
                return (null, 0)!;
            }
            var readCount = _accessor.ReadArray(blockIndex * _blockSize, _buf, _headerSize, _blockSize);
            return (_buf, readCount);
        }

        public byte[] UnsafeRead(long blockIndex, out int readCount)
        {
            readCount = _accessor.ReadArray(blockIndex * _blockSize, _buf, _headerSize, _blockSize);
            return _buf;
        }

        public void Dispose()
        {
            _mmf.Dispose();
            _accessor.Dispose();
        }

        public IEnumerator<(byte[], int, long)> GetEnumerator()
        {
            return new FileBlockEnumerator(this);
        }

        private class FileBlockEnumerator : IEnumerator<(byte[], int, long)>
        {
            private readonly FileBlockReader _reader;
            private long _index;
            private readonly long _max;

            public FileBlockEnumerator(FileBlockReader reader)
            {
                _reader = reader;
                _index = -1;
                _max = reader._max;
            }

            public bool MoveNext()
            {
                _index++;
                return _index < _max;
            }

            public void Reset()
            {
                _index = -1;
            }

            public (byte[], int, long) Current
            {
                get
                {
                    if (0<=_index && _index < _max)
                    {
                        var buf= _reader.UnsafeRead(_index,out var count);
                        return (buf, count, _index);
                    }
                    else
                        return (null, 0, _index)!;
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}