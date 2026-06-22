using Google.Protobuf;
using PubSubLib.Contracts;
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

    public static void EnqueueMessage<T>(string subject, T data, Action<string, byte[]>? notify) where T : class, IMessage
    {
        _channel.Writer.TryWrite(() =>
        {
            var bytes = data.ToByteArray();
            notify?.Invoke(subject, bytes);
        });
    }

    public static void Flush()
    {
        while (_channel.Reader.TryRead(out var action))
        {
            try { action(); }
            catch (Exception ex) { PubSubLog.Error(ex, "MirrorProtoBus.Flush action failed"); }
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
                    catch (Exception ex) { PubSubLog.Error(ex, "MirrorProtoBus.ProcessLoop action failed"); }
                }
            }
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "MirrorProtoBus.ProcessLoop terminated");
        }
    }
}
