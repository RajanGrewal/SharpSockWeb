namespace SharpSockWeb.Lib
{
    internal static class Constant
    {
        public const int WebSocketVersion = 13; //RFC 6055

        public const ulong MaxFrameSize = 1024 * 1000 * 2; //2 Megabytes

        public const string CloseMessage = "Server requested disconnect";

        public const string RequestLinePattern = @"^GET.*HTTP\/1\.1$";
        public const string RequestHeaderPattern = @"^.*: .*$";
        
        public const string WebSockVerHeader = "Sec-WebSocket-Version";
        public const string WebSockKeyHeader = "Sec-WebSocket-Key";
        public const string WebSockPtclHeader = "Sec-WebSocket-Protocol";

        public const string OriginHeader = "Origin";

        public const string HttpBadRequest = "HTTP/1.1 400 Bad Request";
        public const string HttpNotImplemented = "HTTP/1.1 501 Not Implemented";
        
        public const string HttpHandshake =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: {0}\r\n" +
            "\r\n";
    }
}
