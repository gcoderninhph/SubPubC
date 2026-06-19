# Agent Instructions

## MyConnection

MyConnection is an external NuGet dependency (version 1.0.4). When you need information about its interfaces, read the full API reference at:

https://github.com/gcoderninhph/MyConnection/blob/master/README.md

Key interfaces used in this project:
- `IClient` — `SubscribeTcp`/`SubscribeUdp` return `ISubscribe` (has `UnSubscribe()`)
- `IClientModule` — only has `SetIClient(IClient client)`, no Dispose lifecycle
- `ISubscribe` — `void UnSubscribe()` for cleaning up subscriptions
