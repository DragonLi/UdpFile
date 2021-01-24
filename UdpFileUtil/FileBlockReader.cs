using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;

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
        private readonly MemoryMappedViewAccessor _hashReader;
        private readonly byte[] _hashBuf;
        private readonly SHA512Managed _hasher;

        public FileBlockReader(int blockSize, FileInfo srcFile)
        {
            _blockSize = blockSize;
            _srcFile = srcFile;
            _hasher = new SHA512Managed();
            _mmf = MemoryMappedFile.CreateFromFile(srcFile.FullName);
            _accessor = _mmf.CreateViewAccessor();
            _hashReader = _mmf.CreateViewAccessor();
            _hashBuf = new byte[blockSize];
            unsafe
            {
                _headerSize = sizeof(CommandPackage) + sizeof(DataCommandInfo);
            }
            _buf = new byte[_headerSize+blockSize];
            var total = srcFile.Length;
            var max = total / blockSize;
            _max = total - max * blockSize > 0 ? max + 1 : max;
        }

        public long MaxBlockIndex => _max;

        public int HasherLen => _hasher.HashSize / 8;

        public byte[] CalculateHash(long blockIndex)
        {
            var readCount = _hashReader.ReadArray(blockIndex * _blockSize, _hashBuf, 0, _blockSize);
            return _hasher.ComputeHash(_hashBuf, 0, readCount);
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
            _hasher.Dispose();
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