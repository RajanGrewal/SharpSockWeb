using System;

namespace SharpSockWeb.Lib
{
    internal static class HostOrder
    {
        public static ushort UShort(byte[] data)
        {
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }
        public static ulong ULong(byte[] data)
        {
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }
    }
}
