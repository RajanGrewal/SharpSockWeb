namespace SharpSockWeb.Lib
{
    internal static class Constant
    {
        public const int WebSocketVersion = 13;

        public const string RequestLinePattern = @"^GET.*HTTP\/1\.1$";
        public const string RequestHeaderPattern = @"^.*: .*$";
        
        public const string WebSockVerHeader = "Sec-WebSocket-Version";
        public const string WebSockKeyHeader = "Sec-WebSocket-Key";
        public const string WebSockPtclHeader = "Sec-WebSocket-Protocol";

        public const string BadRequest = "HTTP/1.1 400 Bad Request";
        public const string NotImplemented = "HTTP/1.1 501 Not Implemented";

        public const string Handshake =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: {0}\r\n" +
            "\r\n";
    }
}
