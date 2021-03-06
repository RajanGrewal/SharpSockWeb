﻿using System;
using System.Net;
using System.Threading.Tasks;
using SharpSockWeb.Lib;

namespace SharpSockWeb.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger.InitConsole("SharpSockWeb.Server");
            using (var server = new WebSocketServer(IPAddress.Any, 6360, "http://localhost"))
            {
                server.OnClientConnected += (conSock) =>
                {
                    Logger.Write(LogLevel.Connection, "{0} connected",conSock.RemoteEndPoint);
                    Task.Run(conSock.SendPingAsnyc);
                };

                server.OnClientStringReceived += (strSock, strMessage) =>
                {
                    Logger.Write(LogLevel.DataLoad, strMessage);

                    Task.Factory.StartNew(async () =>
                    {
                        await Task.Delay(1000);
                        await strSock.SendStringAsync(strMessage);
                    });

                };

                server.OnClientDisconnected += (disSock) =>
                {
                    Logger.Write(LogLevel.Connection, "{0} disconnected", disSock.RemoteEndPoint);
                };

                server.Start();
                Logger.Write(LogLevel.Server, "Listening on port {0} @ origin {1}",server.Port,server.Origin);
                Logger.Write(LogLevel.Info,"Press enter to exit");
                Console.ReadLine();
            }
        }
    }
}
