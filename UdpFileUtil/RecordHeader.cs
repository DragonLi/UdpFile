namespace UdpFile
{
    public unsafe struct RecordHeader
    {
        public long FileSize;
        public int BlockSize;
        public int HasherSize;
        public int FinishedTag;//sender use as hashing count while receiver use as finished tag
    }
}