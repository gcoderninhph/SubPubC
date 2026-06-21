using Google.Protobuf;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PubSubLib.Mirror;

public static class MirrorProtoBus
{
    private static readonly Channel<Action> _channel = Channel.CreateUnbounded<Action>();

    static MirrorProtoBus()
    {
        _ = Task.Run(ProcessLoop);
    }

    public static void Enqueue<T>(T proto, Action<byte[]> notify, Action<T> setter)
    {
        _channel.Writer.TryWrite(() =>
        {
            setter(proto);
            if (proto is IMessage msg)
            {
                var bytes = msg.ToByteArray();
                notify?.Invoke(bytes);
            }
        });
    }

    public static void Flush()
    {
        while (_channel.Reader.TryRead(out var action))
        {
            try { action(); }
            catch { }
        }
    }

    private static async Task ProcessLoop()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var action))
                {
                    try { action(); }
                    catch { }
                }
            }
        }
        catch
        {
        }
    }
}
