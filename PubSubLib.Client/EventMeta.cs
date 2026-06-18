namespace PubSubLib.Client;

public enum EventTransport { Tcp, Udp }

public readonly struct EventMeta
{
    public EventTransport Transport { get; }
    public EventMeta(EventTransport transport) { Transport = transport; }
}
