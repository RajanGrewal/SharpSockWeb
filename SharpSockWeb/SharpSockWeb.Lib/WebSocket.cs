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
        private readonly NetworkStream m_stream;
        private readonly string m_endPoint;
        private readonly DateTime m_createTime;
        private DateTime m_pingPongTime;
        private SocketState m_state;
        private bool m_requestClose;

        public string RemoteEndPoint => m_endPoint;
        public SocketState State => m_state;
        public DateTime CreateTime => m_createTime;
        public DateTime PongPingTime => m_pingPongTime;

        internal WebSocket(WebSocketServer parent, TcpClient client)
        {
            m_parent = parent;
            m_client = client;
            m_stream = client.GetStream();
            m_endPoint = SetSockOpt(client);
            m_createTime = DateTime.Now;

            m_pingPongTime = DateTime.Now;
            m_state = SocketState.Connecting;
            m_requestClose = false;
        }

        private async Task<byte[]> ReadBytesAsync(ulong readLength)
        {
            if (readLength == 0)
                return new byte[0];

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
                string curRead = Constant.GetString(buf);

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

            //Clients must always have Mask set to true
            //But for this server we will not check for it

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
            frame.ExtLenBytes = await ReadBytesAsync(len);
        }
        internal async Task ReadMaskKey(Frame frame)
        {
            ulong len = frame.MaskLen;
            frame.MaskKey = await ReadBytesAsync(len);
        }
        internal async Task ReadPayload(Frame frame)
        {
            ulong len = frame.FullPayloadLen;

            if (len > Constant.MaxFrameSize)
                throw new InvalidDataException("Payload length exceeds max frame length");

            var payLoad = await ReadBytesAsync(len);

            if (frame.Mask && payLoad.Length > 0)
                Crypto.DecryptPayload(payLoad, frame.MaskKey);

            switch (frame.OpCode)
            {
                case OpCode.Text:
                    var str = Constant.GetString(payLoad);
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
                    await SendPongAsync(payLoad);
                    break;
                case OpCode.Pong:
                    m_pingPongTime = DateTime.Now;
                    break;
                case OpCode.Cont:
                    throw new InvalidDataException("No support for fragmented messages yet");
            }
        }

        public async Task<bool> SendStringAsync(string message)
        {
            var buffer = Constant.GetBytes(message);
            return await SendFrameAsync(OpCode.Text, buffer);
        }
        public async Task<bool> SendDataAsync(byte[] buffer)
        {
            return await SendFrameAsync(OpCode.Binary, buffer);
        }
        public async Task<bool> SendPingAsnyc()
        {
            return await SendFrameAsync(OpCode.Ping, new byte[0]);
        }
        public async Task<bool> SendCloseAsync()
        {
            if (m_state != SocketState.Closed && m_state != SocketState.Closing)
            {
                var payLoad = Constant.GetBytes(Constant.CloseMessage);
                m_requestClose = true;
                return await SendCloseAsync(payLoad);
            }
            return false;
        }

        private async Task<bool> SendCloseAsync(byte[] payLoad)
        {
            bool ret = await SendFrameAsync(OpCode.Close, payLoad);

            if(ret) //TODO: Confirm this
                m_state = SocketState.Closing;

            return ret;
        }
        private async Task<bool> SendPongAsync(byte[] payLoad)
        {
            return await SendFrameAsync(OpCode.Pong, payLoad);
        }
        private async Task<bool> SendFrameAsync(OpCode opCode, byte[] payLoadData)
        {
            if (payLoadData == null)
                throw new ArgumentNullException(nameof(payLoadData));

            int payLoadLen = payLoadData.Length;
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

            if (payLoadLen > 0)
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

            return await SendRawAsync(finData);
        }
        private async Task<bool> SendRawAsync(string message)
        {
            var buffer = Constant.GetBytes(message);
            return await SendRawAsync(buffer);
        }
        private async Task<bool> SendRawAsync(byte[] buffer)
        {
            bool toReturn = true;
            try
            {
                if (m_state == SocketState.Closing || m_state == SocketState.Closed)
                    toReturn = false;
                else
                    await m_stream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception)
            {
                toReturn = false;
            }
            return toReturn;
        }

        public void ForceClose()
        {
            if (m_state == SocketState.Closed)
                return;

            var oldState = m_state;

            m_state = SocketState.Closed;
            m_client.Close();
            m_stream.Close();
            
            if(oldState == SocketState.Open || oldState == SocketState.Closing)
                m_parent.ClientDisconnected(this);
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
