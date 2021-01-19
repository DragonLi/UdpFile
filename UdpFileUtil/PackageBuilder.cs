namespace UdpFile
{
    public static unsafe class PackageBuilder
    {
        private static readonly byte[] StartPackBuf = new byte[sizeof(CommandPackage) + sizeof(StartCommandInfo)];
        private static readonly byte[] StopPackBuf = new byte[sizeof(CommandPackage) + sizeof(StopCommandInfo)];
        private static readonly int DataHeadSize = sizeof(DataCommandInfo);
        public static (byte[], int) PrepareStartPack(ref CommandPackage cmd,ref StartCommandInfo info,string targetFileName)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            cmd.Cmd = CommandEnum.Start;
            var offset = cmd.WriteTo(StartPackBuf, 0);
            info.WriteTo(StartPackBuf, offset, targetFileName);
            return (StartPackBuf, StartPackBuf.Length);
        }

        public static (byte[], int) PrepareDataPack(ref CommandPackage cmd,ref DataCommandInfo info,FileBlockReader blockReader)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            cmd.Cmd = CommandEnum.Data;
            var buf = blockReader.UnsafeRead(info.BlockIndex, out var count);
            var offset = cmd.WriteTo(buf, 0);
            info.WriteTo(buf, offset);
            return (buf, count + offset + DataHeadSize);
        }

        public static (byte[],int) PrepareStopPack(ref CommandPackage cmd, ref StopCommandInfo info)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            cmd.Cmd = CommandEnum.Stop;
            var offset = cmd.WriteTo(StopPackBuf, 0);
            info.WriteTo(StopPackBuf, offset);
            return (StopPackBuf, StopPackBuf.Length);
        }
    }
}