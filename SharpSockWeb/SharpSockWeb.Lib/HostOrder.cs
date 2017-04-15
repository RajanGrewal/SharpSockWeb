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

        public static byte[] ToBytes(ushort value)
        {
            var data = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);

            return data;
        }
        public static byte[] ToBytes(ulong value)
        {
            var data = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);

            return data;
        }
    }
}
