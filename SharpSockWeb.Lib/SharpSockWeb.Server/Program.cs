using System;
using System.Net;
using SharpSockWeb.Lib;

namespace SharpSockWeb.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var server = new WebSocketServer(IPAddress.Any, 6360, "http://localhost"))
            {
                server.OnClientConnected += (conSock) =>
                {
                    Logger.Write(LogLevel.Connection, "{0} connected",conSock.RemoteEndPoint);
                };

                server.OnClientStringReceived += (strSock, strMessage) =>
                {
                    Logger.Write(LogLevel.DataLoad, strMessage);
                };

                server.OnClientDisconnected += (disSock) =>
                {
                    Logger.Write(LogLevel.Connection, "{0} disconnected", disSock.RemoteEndPoint);
                };

                server.Start();
                Console.ReadLine();
            }
        }
    }
}
