using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SharpSockWeb.Lib
{
    public sealed class WebSocket
    {
        private readonly WebSocketServer m_parent;
        private readonly TcpClient m_client;
        private readonly string m_endPoint;
        private readonly NetworkStream m_stream;
        private SocketState m_state;
        private DateTime m_createTime;
        private bool m_requestClose;

        public string RemoteEndPoint => m_endPoint;
        public SocketState State => m_state;
        public DateTime CreateTime => m_createTime;

        internal WebSocket(WebSocketServer parent, TcpClient client)
        {
            m_parent = parent;
            m_client = client;
            m_endPoint = SetSockOpt(client);
            m_stream = client.GetStream();
            m_state = SocketState.Connecting;
            m_createTime = DateTime.Now;
            m_requestClose = false;
        }

        private async Task<byte[]> ReadBytesAsync(ulong readLength)
        {
            if (readLength == 0)
                throw new ArgumentOutOfRangeException(nameof(readLength));

            int idx = 0;
            int len = (int)readLength;
            var buffer = new byte[len];

            while (idx < len)
            {
                idx += await m_stream.ReadAsync(buffer, idx, len - idx);
            }

            return buffer;
        }

        private async Task<string> ReadLineAsync()
        {
            var sb = new StringBuilder();
            string lastRead = string.Empty;

            while (true)
            {
                var buf = await ReadBytesAsync(1);
                string curRead = Encoding.UTF8.GetString(buf);

                if (curRead != "\r" && curRead != "\n")
                {
                    sb.Append(curRead);
                }

                if (lastRead == "\r" && curRead == "\n")
                {
                    return sb.ToString();
                }

                lastRead = curRead;
            }
        }

        internal async Task<bool> ReadHttpHeader()
        {
            var headers = new Dictionary<string, string>();

            string line = await ReadLineAsync();

            if (!Regex.IsMatch(line, Constant.RequestLinePattern))
            {
                goto die;
            }

            //Loop until end of headers
            while ((line = await ReadLineAsync()).Length != 0)
            {
                if (Regex.IsMatch(line, Constant.RequestHeaderPattern))
                {
                    //Always valid - confirmed by regex
                    var arr = line.Split(new[] { ": " }, 2, StringSplitOptions.None);

                    var key = arr[0];
                    var val = arr[1];

                    headers.Add(key, val);
                }
                else
                {
                    goto die; //No idea what is being read
                }
            }

            //No sub protocol support
            if (headers.ContainsKey(Constant.WebSockPtclHeader))
            {
                goto die;
            }

            string strVer, strKey, strOrigin;

            if (headers.TryGetValue(Constant.WebSockVerHeader, out strVer) &&
                headers.TryGetValue(Constant.WebSockKeyHeader, out strKey) &&
                headers.TryGetValue(Constant.OriginHeader, out strOrigin))
            {
                //RFC 6055 check
                if (Constant.WebSocketVersion.ToString() != strVer)
                {
                    goto die;
                }

                //Origin check - TODO: Add IgnoreCase compare ?
                if (strOrigin != m_parent.Origin)
                {
                    goto die;
                }

                var answer = Crypto.GenerateAccept(strKey);
                var respHeader = string.Format(Constant.HttpHandshake, answer);

                await SendRawAsync(respHeader);

                m_state = SocketState.Open;
                m_parent.ClientConnected(this);

                return true;
            }

            die:
            await SendRawAsync(Constant.HttpBadRequest);
            ForceClose();
            return false;
        }
        internal async Task ReadFrameHeader(Frame frame)
        {
            var buffer = await ReadBytesAsync(2);
            byte header1 = buffer[0];
            byte header2 = buffer[1];

            frame.Fin = (header1 & 0x80) == 0x80;
            frame.Rsv1 = (header1 & 0x40) == 0x40;
            frame.Rsv2 = (header1 & 0x20) == 0x20;
            frame.Rsv3 = (header1 & 0x10) == 0x10;
            frame.OpCode = (OpCode)(header1 & 0x0f);
            frame.Mask = (header2 & 0x80) == 0x80;
            frame.PayloadLen = (byte)(header2 & 0x7f);

            if (!Enum.IsDefined(typeof(OpCode), frame.OpCode))
                throw new InvalidDataException("OpCode is not valid");

            switch (frame.OpCode)
            {
                case OpCode.Close:
                case OpCode.Ping:
                case OpCode.Pong:

                    if (!frame.Fin)
                        throw new InvalidDataException("Control frame payload is segmented");

                    if (frame.PayloadLen > 125)
                        throw new InvalidDataException("Control frame payload length is extended");

                    break;
            }

            //TODO: Rsv Checks
        }
        internal async Task ReadExtLen(Frame frame)
        {
            ulong len = frame.ExtLen;

            if (len == 0)
                return;

            frame.ExtLenBytes = await ReadBytesAsync(len);
        }
        internal async Task ReadMaskKey(Frame frame)
        {
            ulong len = (ulong)(frame.Mask ? 4 : 0);

            if (len == 0)
                return;

            frame.MaskKey = await ReadBytesAsync(len);
        }
        internal async Task ReadPayload(Frame frame)
        {
            ulong len = frame.FullPayloadLen;

            if (len > Constant.MaxFrameSize)
                throw new InvalidDataException("Payload length exceeds max frame length");

            byte[] payLoad = null;

            //if (len > 0)
            {
                payLoad = await ReadBytesAsync(len);

                if (frame.Mask)
                    Crypto.DecryptPayload(payLoad, frame.MaskKey);
            }

            switch (frame.OpCode)
            {
                case OpCode.Text:
                    var str = Encoding.UTF8.GetString(payLoad);
                    m_parent.ClientString(this, str);
                    break;
                case OpCode.Binary:
                    m_parent.ClientData(this, payLoad);
                    break;
                case OpCode.Close:
                    if (!m_requestClose)
                        await SendCloseAsync(payLoad); //Echo the payload
                    ForceClose();
                    break;
                case OpCode.Ping:
                    break;
                case OpCode.Pong:
                    break;
                case OpCode.Cont:
                    throw new InvalidDataException("No support for fragmented messages yet");
            }
        }

        public void ForceClose()
        {
            m_state = SocketState.Closed;
            m_client.Close();
            m_stream.Close();
            m_parent.ClientDisconnected(this);
        }
        public async Task SendCloseAsync()
        {
            var payLoad = Encoding.UTF8.GetBytes(Constant.CloseMessage);
            m_requestClose = true;
            await SendCloseAsync(payLoad);
        }
        private async Task SendCloseAsync(byte[] payLoad)
        {
            m_state = SocketState.Closing;
            await SendFrameAsync(OpCode.Close, payLoad);
        }

        public async Task SendString(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await SendFrameAsync(OpCode.Text, buffer);
        }
        public async Task SendData(byte[] buffer)
        {
            await SendFrameAsync(OpCode.Binary, buffer);
        }

        private async Task SendFrameAsync(OpCode opCode, byte[] payLoadData)
        {
            int payLoadLen = payLoadData.Length;

            //TODO: Confirm zero length payloads are invalid
            if (payLoadLen == 0)
                throw new ArgumentOutOfRangeException(nameof(payLoadData), "Zero length payload");

            int finLen = 2 + payLoadLen;

            byte[] extData = null;
            int extLen = 0;

            //Needs extended length
            if (payLoadLen > 125)
            {
                extData = payLoadLen > ushort.MaxValue ?
                    HostOrder.ToBytes((ulong)payLoadLen) :
                    HostOrder.ToBytes((ushort)payLoadLen);

                extLen = extData.Length;
                finLen += extLen;
            }

            byte[] finData = new byte[finLen];

            if (extData != null)
            {
                Buffer.BlockCopy(extData, 0, finData, 2, extLen);
            }

            Buffer.BlockCopy(payLoadData, 0, finData, 2 + extLen, payLoadLen);


            if (payLoadLen > 125) //Fix payload len
                payLoadLen = payLoadData.Length > ushort.MaxValue ? 127 : 126;

            byte header1 = 0x80; //Fin = true
            header1 |= (byte)((int)opCode & 0x0f);
            //header1 |= 0x40; //Rsv1 = true;
            //header1 |= 0x20; //Rsv2 = true;
            //header1 |= 0x10; //Rsv3 = true;

            byte header2 = (byte)(payLoadLen & 0x7f);
            //header2 |= 0x80; //Mask = true;

            finData[0] = header1;
            finData[1] = header2;

            await SendRawAsync(finData);
        }
        private async Task SendRawAsync(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await SendRawAsync(buffer);
        }
        private async Task SendRawAsync(byte[] buffer)
        {
            await m_stream.WriteAsync(buffer, 0, buffer.Length);
        }

        private static string SetSockOpt(TcpClient client)
        {
            string endpoint = "Error";
            var sock = client.Client;

            try
            {
                endpoint = sock.RemoteEndPoint.ToString();
                sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                sock.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            }
            catch (SocketException)
            {
                /*Socket No Longer Connected*/
            }

            return endpoint;
        }
    }
}
