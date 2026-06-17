using System.Threading.Channels;

namespace PubSubLib;

internal sealed class EventChannel<T> : IDisposable where T : class
{
    private readonly Channel<Action> _channel;
    private readonly ChannelWriter<Action> _writer;
    private readonly ChannelReader<Action> _reader;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts;

    internal Action<(List<long>, IUnit<T>)>? OnUnitEnterBatch;
    internal Action<(List<long>, IUnit<T>)>? OnUnitLeaveBatch;
    internal Action<(long, List<IUnit<T>>)>? OnUnitEnterSync;
    internal Action<(long, List<UnitKey>)>? OnUnitLeaveSync;
    internal Action<(List<long>, IUnit<T>, string, object)>? OnUnitEvent;

    internal Action<IUnit<T>, List<long>>? AfterBatchEnter;
    internal Action<IUnit<T>, List<long>>? AfterBatchLeave;
    internal Action<long, List<IUnit<T>>>? AfterSyncEnter;
    internal Action<long, List<UnitKey>>? AfterSyncLeave;
    internal Action<IUnit<T>, List<long>, string, object?>? AfterUnitEvent;

    public EventChannel()
    {
        _channel = Channel.CreateUnbounded<Action>();
        _writer = _channel.Writer;
        _reader = _channel.Reader;
        _cts = new CancellationTokenSource();
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "PubSubLib.EventChannel"
        };
    }

    public void Start() => _worker.Start();

    public void Enqueue(Action action)
    {
        _writer.TryWrite(action);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _writer.Complete();
        _worker.Join(3000);
        _cts.Dispose();
    }

    private void WorkerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _reader.WaitToReadAsync(_cts.Token).AsTask().Wait();
            }
            catch { break; }

            while (_reader.TryRead(out var action))
            {
                try { action(); }
                catch { }
            }
        }
    }
}
