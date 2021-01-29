using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace UdpFile
{
    public enum CommandEnum:byte
    {
        Start,
        StartAck,
        Stop,
        Data,
        DataRestart,
        Verify,
        VerifyRestart,
        DataProgress,
        VerifyProgress,
    }

    public enum OverrideModeEnum : byte
    {
        NewOrFail,
        Resume,
        Rename,
        Override,
    }

    public enum StartError : byte
    {
        PassCheck,
        VersionNotCompatible,
        InvalidTargetFileName,
        TargetFileExist,
        TargetFileIsDirectory,
        RecordPartExistOrDir,
    }

    public enum StopErrEnum : byte
    {
        Success,
        FileError,
        Timeout,
    }

    public static class EnumHelper
    {
        public static OverrideModeEnum FromInt(int modeInt)
        {
            return (OverrideModeEnum) modeInt;
        }
    }

    public static class BinSerializableHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void WriteTo<T>(byte[] buf, int start,ref T input, int size)
        {
            var target = new Span<byte>(buf, start, size);
            var src = new Span<byte>(Unsafe.AsPointer(ref input), size);
            src.CopyTo(target);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void ReadFrom<T>(byte[] buf, int start, ref T output, int size)
        where T:struct
        {
            var src = new Span<byte>(buf, start, size);
            var target = new Span<byte>(Unsafe.AsPointer(ref output), size);
            src.CopyTo(target);
        }
    }
    
    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct CommandPackage
    {
        private static readonly int _size = sizeof(CommandPackage);
        public int ReadFrom(byte[] buf, int start)
        {
            if (_size + start > buf.Length)
                return 0;
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
            return _size;
        }

        public int WriteTo(byte[] buf, int start)
        {
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
            return _size;
        }
        public int SeqId;
        public CommandEnum Cmd;
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct StartCommandInfo
    {
        private static readonly int _size = sizeof(StartCommandInfo);
        public string ReadFrom(byte[] buf, int start)
        {
            if (_size + start > buf.Length)
            {
                TargetFileSize = 0;
                BlockSize = TargetFileNameLength = 0;
                return string.Empty;
            }
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
            return TargetFileNameLength <= 0 ? string.Empty : Encoding.UTF8.GetString(buf, start + _size, TargetFileNameLength);
        }

        public int WriteTo(byte[] buf, int start, string targetFileName)
        {
            TargetFileNameLength =
                Encoding.UTF8.GetBytes(targetFileName, 0, targetFileName.Length, buf, start + _size);
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
            return _size + TargetFileNameLength;
        }

        public long TargetFileSize;
        public int BlockSize;
        public byte Version;
        public OverrideModeEnum OverrideMode;
        public int ClientPort;
        public int TargetFileNameLength;
        //can not add directly as a member: string TargetFileName;
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct StartAckInfo
    {
        private static readonly int _size = sizeof(StartAckInfo);
        
        public int AckSeqId;
        public int Port;
        public StartError Err;
        public int RenameFileNameLen;
        // rename file name to string followed, zero mean dot not rename
        public string ReadFrom(byte[] buf, int start,out bool isSuccess)
        {
            if (_size + start > buf.Length)
            {
                AckSeqId = Port = RenameFileNameLen = 0;
                isSuccess = false;
                return string.Empty;
            }
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);

            isSuccess = true;
            return RenameFileNameLen <= 0 ? string.Empty : Encoding.UTF8.GetString(buf, start + _size, RenameFileNameLen);
        }
        
        public int WriteTo(byte[] buf, int start, string renameFileName)
        {
            RenameFileNameLen =
                Encoding.UTF8.GetBytes(renameFileName, 0, renameFileName.Length, buf, start + _size);
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
            return _size + RenameFileNameLen;
        }
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct StopCommandInfo
    {
        private static readonly int _size = sizeof(StopCommandInfo);
        public void ReadFrom(byte[] buf, int start)
        {
            if (_size + start > buf.Length)
                return;
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
        }

        public void WriteTo(byte[] buf, int start)
        {
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
        }

        public StopErrEnum ErrorCode;
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct DataCommandInfo
    {
        private static readonly int _size = sizeof(DataCommandInfo);
        public int ReadFrom(byte[] buf, int start)
        {
            if (_size + start > buf.Length)
                return 0;
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
            return _size;
        }

        public void WriteTo(byte[] buf, int start)
        {
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
        }

        public int BlockIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VerifyCommandInfo
    {
        private static readonly int _size = sizeof(VerifyCommandInfo);
        public byte[] ReadFrom(byte[] buf, int start)
        {
            if (_size + start > buf.Length)
            {
                //BlockIndex = Length = 0;
                return Array.Empty<byte>();
            }
            
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
            if (Length <= 0)
            {
                return Array.Empty<byte>();
            }

            var hash = new byte[Length];
            Buffer.BlockCopy(buf, start + _size, hash, 0, Length);
            return hash;
        }
        public void WriteTo(byte[] buf, int start,byte[] verificationList)
        {
            Length = verificationList.Length;
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
            Buffer.BlockCopy(verificationList, 0, buf, start + _size, Length);
        }

        public int BlockIndex;
        public int Length;
        //can not add directly as a member: byte[] hash;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VerifyAckInfo
    {
        private static readonly int _size = sizeof(VerifyAckInfo);
        public int e0;
        public int e1;
        public int e2;
        public int e3;
        public int e4;
        public int e5;
        public int e6;
        public int e7;
        public int e8;
        public int e9;
        public int e10;
        public int e11;
        public int e12;
        public int e13;
        public int e14;
        public int e15;
        public void ReadFrom(byte[] buf, int start)
        {
            if (_size+start > buf.Length)
            {
                e0 = e1 = e2 = e3 = e4 = e5 = e6 = e7 = e8 = e9 = e10 = e11 = e12 = e13 = e14 = e15 = -1;
                return;
            }
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
        }

        public void WriteTo(byte[] buf, int start)
        {
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct AckIndex
    {
        private static readonly int _size = sizeof(AckIndex);
        public int ReadFrom(byte[] buf, int start)
        {
            if (_size + start > buf.Length)
                return 0;
            BinSerializableHelper.ReadFrom(buf, start, ref this, _size);
            return _size;
        }

        public void WriteTo(byte[] buf, int start)
        {
            BinSerializableHelper.WriteTo(buf, start, ref this, _size);
        }

        public int Index;//use by DataRestart or VerifyRestart, -1 means no need to restart
    }
}