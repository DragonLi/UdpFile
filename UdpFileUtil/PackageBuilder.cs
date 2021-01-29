namespace UdpFile
{
    public static unsafe class PackageBuilder
    {
        private static readonly int CmdSize = sizeof(CommandPackage);
        private static readonly int DataCmdSize = sizeof(DataCommandInfo);
        private static readonly int StartCmdSize = sizeof(StartCommandInfo);
        private static readonly int VerifyCmdSize = sizeof(VerifyCommandInfo);
        private static readonly int StartAckSize = sizeof(StartAckInfo);
        private static readonly int StopCmdSize = sizeof(StopCommandInfo);
        public static readonly int DataHeadSize = CmdSize + DataCmdSize;
        
        public static (byte[], int) PrepareStartPack(ref CommandPackage cmd,ref StartCommandInfo info,string targetFileName)
        {
            var size = CmdSize + StartCmdSize + 4 * targetFileName.Length;
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
            var buf = new byte[CmdSize + StopCmdSize];
            var offset = cmd.WriteTo(buf, 0);
            info.WriteTo(buf, offset);
            return (buf, buf.Length);
        }

        public static (byte[], int) PrepareHashPack(ref CommandPackage cmd, ref VerifyCommandInfo vPack,
            byte[] hashBuf, int index)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            vPack.BlockIndex = index;
            var buf = new byte[CmdSize + VerifyCmdSize + hashBuf.Length];
            var offset =cmd.WriteTo(buf, 0);
            vPack.WriteTo(buf, offset, hashBuf);
            return (buf, buf.Length);
        }

        public static (byte[], int) PrepareStartAckPack(ref CommandPackage cmd, ref StartAckInfo ack, string rename)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            var buf = new byte[CmdSize + StartAckSize + 4 * rename.Length];
            var offset = cmd.WriteTo(buf, 0);
            offset += ack.WriteTo(buf, offset, rename);
            return (buf, offset);
        }

        public static (byte[], int) PrepareAskVerifyProgress(ref CommandPackage cmd)
        {
            cmd.SeqId = TransportSeqFactory.NextId();
            var buf = new byte[CmdSize];
            var offset = cmd.WriteTo(buf, 0);
            return (buf, offset);
        }
    }
}