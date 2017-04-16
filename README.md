# SharpSockWeb
Very bareboned C# WebSocket server (RFC 6455)

## Notes
* Only supports **Sec-WebSocket-Version** 13
* No support for **Sec-WebSocket-Protocol** yet
* No support for **Segmented Frames** yet
* No support for **RSV Values**
* Allows Client->Server frames without a **KeyMask** (RFC spec does not allow this)
* Other than whats listed above, it is complete!
