using System;
using System.Runtime.InteropServices;
using System.Text;

namespace UdpFile
{
    public enum CommandEnum:byte
    {
        Start,
        Stop,
        Data,
        Verify,
        Ack,
        AckList,
    }

    public static class BinSerializableHelper
    {
        public static unsafe void WriteTo(byte[] buf, int start,void* input, int size)
        {
            var target = new Span<byte>(buf, start, size);
            var src = new Span<byte>(input, size);
            src.CopyTo(target);
        }

        public static unsafe void ReadFrom(byte[] buf, int start, void* output, int size)
        {
            var src = new Span<byte>(buf, start, size);
            var target = new Span<byte>(output, size);
            src.CopyTo(target);
        }
    }
    
    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct CommandPackage
    {
        public int ReadFrom(byte[] buf, int start)
        {
            var size = sizeof(CommandPackage);
            fixed (void* t = &this)
            {
                BinSerializableHelper.ReadFrom(buf, start, t,size);
            }
            return size;
        }

        public void WriteTo(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                BinSerializableHelper.WriteTo(buf, start, t,sizeof(CommandPackage));
            }
        }
        public int SeqId;
        public CommandEnum Cmd;
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct StartCommandInfo
    {
        public string ReadFrom(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                var size = sizeof(StartCommandInfo);
                BinSerializableHelper.ReadFrom(buf, start, t, size);
                return TargetFileNameLength <= 0 ? string.Empty : Encoding.UTF8.GetString(buf, start + size, TargetFileNameLength);
            }
        }

        public void WriteTo(byte[] buf, int start, string targetFileName)
        {
            fixed (void* t = &this)
            {
                var size = sizeof(StartCommandInfo);
                TargetFileNameLength =
                    Encoding.UTF8.GetBytes(targetFileName, 0, targetFileName.Length, buf, start + size);
                BinSerializableHelper.WriteTo(buf, start, t,size);
            }
        }

        public long TargetFileSize;
        public int BlockSize;
        public int TargetFileNameLength;
        //can not add directly as a member: string TargetFileName;
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct StopCommandInfo
    {
        public void ReadFrom(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                BinSerializableHelper.ReadFrom(buf, start, t,sizeof(StopCommandInfo));
            }
        }

        public void WriteTo(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                BinSerializableHelper.WriteTo(buf, start, t,sizeof(StopCommandInfo));
            }
        }

        public byte ErrorCode;
    }

    [StructLayout(LayoutKind.Sequential,Pack = 1)]
    public unsafe struct DataCommandInfo
    {

        public int ReadFrom(byte[] buf, int start)
        {
            var size = sizeof(DataCommandInfo);
            fixed (void* t = &this)
            {
                BinSerializableHelper.ReadFrom(buf, start, t,size);
            }
            return size;
        }

        public void WriteTo(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                BinSerializableHelper.WriteTo(buf, start, t,sizeof(DataCommandInfo));
            }
        }

        public long BlockIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VerifyCommandInfo
    {

        public byte[] ReadFrom(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                var size = sizeof(VerifyCommandInfo);
                BinSerializableHelper.ReadFrom(buf, start, t,size);
                if (Length <= 0)
                {
                    return Array.Empty<byte>();
                }
                else
                {
                    var verificationList = new byte[Length];
                    Buffer.BlockCopy(buf, start + size, verificationList, 0, Length);
                    return verificationList;
                }
            }
        }

        public void WriteTo(byte[] buf, int start,byte[] verificationList)
        {
            fixed (void* t = &this)
            {
                Length = verificationList.Length;
                var size = sizeof(VerifyCommandInfo);
                BinSerializableHelper.WriteTo(buf, start, t,size);
                Buffer.BlockCopy(verificationList, 0, buf, start + size, Length);
            }
        }

        public byte Number;
        public int Length;
        //can not add directly as a member: byte[] VerificationList;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct AckCommandInfo
    {

        public void ReadFrom(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                BinSerializableHelper.ReadFrom(buf, start, t,sizeof(AckCommandInfo));
            }
        }

        public void WriteTo(byte[] buf, int start)
        {
            fixed (void* t = &this)
            {
                BinSerializableHelper.WriteTo(buf, start, t,sizeof(AckCommandInfo));
            }
        }

        public int SeqId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AckListCommandInfo
    {
        public byte Number;
        public int Length;
        //can not add directly as a member: int[] SeqId;
    }
}