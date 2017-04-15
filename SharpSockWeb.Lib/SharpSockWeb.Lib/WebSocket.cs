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
        }

        //TODO: Make this support reading ulong 
        private async Task<byte[]> ReadBytes(ulong readLength)
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

        internal async Task ReadHttpHeader()
        {
            //Temp sanitary check
            if (m_state != SocketState.Connecting)
                return;

            var reader = new StreamReader(m_stream);
            var writer = new StreamWriter(m_stream) { AutoFlush = true };

            bool httpAuth = false;
            var headers = new Dictionary<string, string>();

            //Temporary for now - need to clean the whole thing up still
            while (true)
            {
                string line = await reader.ReadLineAsync();

                //Temporary for now
                if (line == null)
                {
                    await Task.Delay(100);
                    continue;
                }

                if (!httpAuth)
                {
                    if (!Regex.IsMatch(line, Constant.RequestLinePattern))
                    {
                        await writer.WriteAsync(Constant.BadRequest);
                        throw new InvalidDataException("Bad HTTP request line");
                    }
                    httpAuth = true;
                }
                else
                {
                    if (Regex.IsMatch(line, Constant.RequestHeaderPattern))
                    {
                        var arr = line.Split(new[] { ": " }, 2, StringSplitOptions.None);
                        headers.Add(arr[0], arr[1]);
                    }
                    else if (line.Length == 0)
                    {
                        //No sub protocol support
                        if (headers.ContainsKey(Constant.WebSockPtclHeader))
                        {
                            await writer.WriteAsync(Constant.BadRequest);
                            throw new InvalidDataException("No protocol support yet");
                        }

                        string strVer;
                        string strKey;

                        if (headers.TryGetValue(Constant.WebSockVerHeader, out strVer) && headers.TryGetValue(Constant.WebSockKeyHeader, out strKey))
                        {
                            if (Constant.WebSocketVersion.ToString() != strVer)
                            {
                                await writer.WriteAsync(Constant.BadRequest);
                                throw new InvalidDataException("Unsupported websocket version");
                            }

                            var answer = Crypto.GenerateAccept(strKey);
                            var respHeader = string.Format(Constant.Handshake, answer);

                            await writer.WriteAsync(respHeader); 

                            m_state = SocketState.Open;
                            m_parent.ClientConnected(this);

                            break; //Out the loop
                        }

                        //Not header match or blank line
                        await writer.WriteAsync(Constant.BadRequest);
                        throw new InvalidDataException("Bad websock header info");
                    }
                    else
                    {
                        await writer.WriteAsync(Constant.BadRequest);
                        throw new InvalidDataException("Ambiguous header data");
                    }
                }
            }

        }
        internal async Task ReadFrameHeader(Frame frame)
        {
            byte[] buffer = await ReadBytes(2);
            byte header1 = buffer[0];
            byte header2 = buffer[1];

            frame.Fin = (header1 & 0x80) == 0x80;
            frame.Rsv1 = (header1 & 0x40) == 0x40;
            frame.Rsv2 = (header1 & 0x20) == 0x20;
            frame.Rsv3 = (header1 & 0x10) == 0x10;
            frame.OpCode = (OpCode)(header1 & 0x0f);
            frame.Mask = (header2 & 0x80) == 0x80;
            frame.PayloadLen = (byte)(header2 & 0x7f);

            if (!frame.Mask)
                throw new InvalidDataException("Mask bit is disabled");

            if (!Enum.IsDefined(typeof(OpCode), frame.OpCode))
                throw new InvalidDataException("OpCode is not valid");

            //TODO: Rsv checks based on OpCode
            //TODO: Control OpCode length check
            //TODO: Control OpCode fragment check
        }
        internal async Task ReadExtLen(Frame frame)
        {
            ulong len = frame.ExtLen;

            frame.ExtLenBytes = new byte[len];

            if (len == 0)
                return;

            frame.ExtLenBytes = await ReadBytes(len);
        }
        internal async Task ReadMaskKey(Frame frame)
        {
            ulong len = (ulong)(frame.Mask ? 4 : 0);

            frame.MaskKey = new byte[len];

            if (len == 0)
                return;

            frame.MaskKey = await ReadBytes(len);
        }
        internal async Task ReadPayload(Frame frame)
        {
            ulong len = 0;

            switch (frame.ExtLenBytes.Length)
            {
                case 0:
                    len = frame.PayloadLen;
                    break;
                case 2:
                    len = HostOrder.UShort(frame.ExtLenBytes);
                    break;
                case 8:
                    len = HostOrder.ULong(frame.ExtLenBytes);
                    break;
            }

            byte[] payLoad = await ReadBytes(len);
            Crypto.DecryptPayload(payLoad, frame.MaskKey);

            switch (frame.OpCode)
            {
                case OpCode.Text:
                    var str = Encoding.UTF8.GetString(payLoad);
                    m_parent.ClientString(this, str);
                    break;
                case OpCode.Binary:
                    m_parent.ClientData(this, payLoad);
                    break;
            }
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
