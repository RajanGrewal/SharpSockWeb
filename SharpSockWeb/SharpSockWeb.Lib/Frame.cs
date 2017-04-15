namespace SharpSockWeb.Lib
{
    internal sealed class Frame
    {
        public bool Fin { get; set; }
        public bool Rsv1 { get; set; }
        public bool Rsv2 { get; set; }
        public bool Rsv3 { get; set; }
        public OpCode OpCode { get; set; }
        public bool Mask { get; set; }
        public byte PayloadLen { get; set; }
        public byte[] ExtLenBytes { get; set; }
        public byte[] MaskKey { get; set; }

        public ulong ExtLen
        {
            get
            {
                if (PayloadLen == 126)
                    return 2;

                if (PayloadLen == 127)
                    return 8;

                return 0;
            }
        }

        public ulong FullPayloadLen
        {
            get
            {
                ulong extLen = ExtLen;

                if (extLen == 2)
                    return HostOrder.UShort(ExtLenBytes);

                if (extLen == 8)
                    return HostOrder.ULong(ExtLenBytes);

                return PayloadLen;
            }
        }

        public ulong MaskLen => (ulong)(Mask ? 4 : 0);
    }
}
