namespace UdpFile
{
    public static unsafe class PackageBuilder
    {
        private static readonly int DataHeadSize = sizeof(CommandPackage) + sizeof(DataCommandInfo);
        public static (byte[], int) PrepareStartPack(ref CommandPackage cmd,ref StartCommandInfo info,string targetFileName)
        {
            var size = sizeof(CommandPackage) + sizeof(StartCommandInfo) + 4 * targetFileName.Length;
            var startPackBuf = new byte[size];
            cmd.SeqId = TransportSeqFactory.NextId();
            cmd.Cmd = CommandEnum.Start;
            var offset = cmd.WriteTo(startPackBuf, 0);
            offset += info.WriteTo(startPackBuf, offset, targetFileName);
            return (startPackBuf, offset);
        }

        public static (byte[], int) PrepareDataPack(ref CommandPackage cmd,ref DataCommandInfo info,FileBlockReader blockReader)
        {
            cmd.SeqId = 0;
            cmd.Cmd = CommandEnum.Data;
            var buf = blockReader.UnsafeRead(info.BlockIndex, out var count);
            var offset = cmd.WriteTo(buf, 0);
            info.WriteTo(buf, offset);
            return (buf, count + DataHeadSize);
        }

        public static (byte[],int) PrepareStopPack(ref CommandPackage cmd, ref StopCommandInfo info)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            cmd.Cmd = CommandEnum.Stop;
            var stopPackBuf = new byte[sizeof(CommandPackage) + sizeof(StopCommandInfo)];
            var offset = cmd.WriteTo(stopPackBuf, 0);
            info.WriteTo(stopPackBuf, offset);
            return (stopPackBuf, stopPackBuf.Length);
        }

        public static (byte[], int) PrepareHashPack(ref CommandPackage cmd, ref VerifyCommandInfo vPack,
            byte[] hashBuf, long index)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            vPack.BlockIndex = index;
            var vBuf = new byte[sizeof(CommandPackage) + sizeof(VerifyCommandInfo) + hashBuf.Length];
            var offset =cmd.WriteTo(vBuf, 0);
            vPack.WriteTo(vBuf, offset, hashBuf);
            return (vBuf, vBuf.Length);
        }
    }
}