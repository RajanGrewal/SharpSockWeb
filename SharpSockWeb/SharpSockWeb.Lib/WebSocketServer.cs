using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpSockWeb.Lib
{
    public class WebSocketServer : IDisposable
    {
        private readonly TcpListener m_listener;
        //private readonly List<WebSocket> m_pending;
        private readonly List<WebSocket> m_clients;
        private readonly object m_lock;
        private readonly int m_port;
        private readonly string m_origin;
        private bool m_active;
        private bool m_disposed;

        public bool Active => m_active;
        public string Origin => m_origin;

        public event Action<WebSocket> OnClientConnected;
        public event Action<WebSocket, byte[]> OnClientDataReceived;
        public event Action<WebSocket, string> OnClientStringReceived;
        public event Action<WebSocket> OnClientDisconnected;

        public WebSocketServer(IPAddress localAddr, int port, string origin)
        {
            m_listener = new TcpListener(localAddr, port);
            m_clients = new List<WebSocket>();
            m_lock = new object();
            m_port = port;
            m_origin = origin;
            m_active = false;
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (m_active)
                throw new InvalidOperationException("Server already active");

            m_active = true;

            m_listener.Start();
            BeginAccept();
        }
        public void Stop()
        {
            ThrowIfDisposed();

            if (!m_active)
                throw new InvalidOperationException("Server is not active");

            m_active = false;
            m_listener.Stop();
        }

        private void BeginAccept()
        {
            if (m_active && !m_disposed)
                m_listener.BeginAcceptTcpClient(EndAccept, null);
        }
        private void EndAccept(IAsyncResult iar)
        {
            if (m_active)
            {
                var client = m_listener.EndAcceptTcpClient(iar);
                Task.Factory.StartNew(ClientLoop, client);
                BeginAccept();
            }
        }

        private async void ClientLoop(object state)
        {
            var tcp = state as TcpClient;
            var sock = new WebSocket(this, tcp);

            Debug.Assert(tcp != null, "Bad thread state object");

            try
            {
                bool httpSuccess = await sock.ReadHttpHeader();

                if(!httpSuccess)
                    return;

                while (tcp.Connected)
                {
                    if (sock.State == SocketState.Closed)
                        break;

                    var frame = new Frame();

                    await sock.ReadFrameHeader(frame);
                    await sock.ReadExtLen(frame);
                    await sock.ReadMaskKey(frame);
                    await sock.ReadPayload(frame);
                }
            }
            catch (Exception e)
            {
                Debug.Fail(e.ToString());
            }
            finally
            {
            //
            }
        }

        //For WebSocket to trigger events
        internal void ClientConnected(WebSocket x) => OnClientConnected?.Invoke(x);
        internal void ClientString(WebSocket x, string s) => OnClientStringReceived?.Invoke(x, s);
        internal void ClientData(WebSocket x, byte[] b) => OnClientDataReceived?.Invoke(x, b);
        internal void ClientDisconnected(WebSocket x) => OnClientDisconnected?.Invoke(x);

        private void ThrowIfDisposed()
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                GC.SuppressFinalize(this);

                if (m_active)
                    m_listener.Stop();

                m_active = false;
            }
        }
    }
}
