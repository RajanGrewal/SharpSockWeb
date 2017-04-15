using System;
using System.Security.Cryptography;
using System.Text;

namespace SharpSockWeb.Lib
{
    internal static class Crypto
    {
        private const string ServerKey = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        public static string GenerateAccept(string clientKey)
        {
            using (var hasher = SHA1.Create())
            {
                var rawKey = string.Concat(clientKey, ServerKey);
                var txtBuf = Encoding.UTF8.GetBytes(rawKey);
                var encBuf = hasher.ComputeHash(txtBuf);
                return Convert.ToBase64String(encBuf);
            }
        }
        public static void DecryptPayload(byte[] payLoad, byte[] maskKey)
        {
            if (payLoad.Length > 0 && maskKey.Length == 4)
                for (int i = 0; i < payLoad.Length; i++)
                    payLoad[i] ^= maskKey[i % 4];
        }
    }
}
