using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using PubSubLib.Contracts;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PubSubLib.Mirror
{

public static class MirrorProtoBus
{
    private static readonly Channel<Action> _channel = Channel.CreateUnbounded<Action>();
    private static volatile int _suppressCount;
    private static Task? _task;

    static MirrorProtoBus()
    {
        _task ??= Task.Run(ProcessLoop).ContinueWith(_ => { });
    }

    public static IDisposable SuppressBackground()
    {
        Interlocked.Increment(ref _suppressCount);
        return new SuppressDisposable();
    }

    private sealed class SuppressDisposable : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref _suppressCount);
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

    public static void EnqueueMessage<T>(string subject, T data, Action<string, byte[]>? notify)
        where T : class, IMessage
    {
        _channel.Writer.TryWrite(() =>
        {
            var bytes = data.ToByteArray();
            notify?.Invoke(subject, bytes);
        });
    }

    public static void Flush()
    {
        _task ??= Task.Run(ProcessLoop).ContinueWith(_ => { });
    }

    private static async Task ProcessLoop()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync())
            {
                if (Volatile.Read(ref _suppressCount) > 0)
                {
                    await Task.Delay(10);
                    continue;
                }

                while (reader.TryRead(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        PubSubLog.Error(ex, "MirrorProtoBus.ProcessLoop action failed");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "MirrorProtoBus.ProcessLoop terminated");
        }
    }
}
}
