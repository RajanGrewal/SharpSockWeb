using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SharpSockWeb.Lib
{
    public class WebSocketServer : IDisposable
    {
        private readonly TcpListener m_listener;
        private readonly List<WebSocket> m_clients;
        private readonly object m_lock;
        private readonly int m_port;
        private readonly string m_origin;
        private bool m_active;
        private bool m_disposed;

        public bool Active => m_active;
        public string Origin => m_origin;
        public int Port => m_port;

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

        //Init methods
        public void Start()
        {
            ThrowIfDisposed();

            if (m_active)
                throw new InvalidOperationException("Server already active");

            m_active = true;

            m_listener.Start();
            BeginAccept();
            Task.Factory.StartNew(WatchTask);
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
                Task.Factory.StartNew(ClientTask, client);
                BeginAccept();
            }
        }

        //Async Loops
        private async void ClientTask(object state)
        {
            var tcp = state as TcpClient;
            var sock = new WebSocket(this, tcp);

            if (tcp == null)
                throw new InvalidOperationException("Bad thread state");

            lock (m_lock)
                m_clients.Add(sock);

            try
            {
                bool httpSuccess = await sock.ReadHttpHeader();

                if (!httpSuccess)
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
#if DEBUG
            catch (Exception e)
            {

                throw;

            }
#endif
            finally
            {
                sock.ForceClose();

                lock (m_lock)
                    m_clients.Remove(sock);
            }
        }
        private async void WatchTask()
        {
            while (m_active)
            {
                await Task.Delay(15 * 1000); //15 Seconds

                WebSocket[] clients;

                lock (m_lock)
                    clients = m_clients.ToArray();

                foreach (var sock in clients)
                {
                    if (sock.State == SocketState.Connecting)
                    {
                        if ((DateTime.Now - sock.CreateTime).Seconds >= 10)
                        {
                            //Socket did not finish handshake in 10 seconds
                            sock.ForceClose();
                        }
                    }
                    else if (sock.State == SocketState.Open)
                    {
                        if ((DateTime.Now - sock.PongPingTime).Seconds >= 30)
                        {
                            //Send ping after 30 seconds
                            await sock.SendPingAsnyc();
                        }
                    }
                }

            }
        }

        //For WebSocket to trigger events
        internal void ClientConnected(WebSocket x) => OnClientConnected?.Invoke(x);
        internal void ClientString(WebSocket x, string s) => OnClientStringReceived?.Invoke(x, s);
        internal void ClientData(WebSocket x, byte[] b) => OnClientDataReceived?.Invoke(x, b);
        internal void ClientDisconnected(WebSocket x) => OnClientDisconnected?.Invoke(x);

        public IEnumerable<WebSocket> GetClients()
        {
            lock (m_lock)
                foreach (var sock in m_clients)
                    if (sock.State == SocketState.Open)
                        yield return sock;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
        public void Dispose()
        {
            if (m_disposed)
                return;

            m_disposed = true;
            GC.SuppressFinalize(this);

            if (m_active)
                m_listener.Stop();

            m_active = false;
        }
    }
}
