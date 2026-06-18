using System.Threading.Channels;

namespace PubSubLib;

internal sealed class EventChannel : IDisposable
{
    private readonly Channel<Action> _channel;
    private readonly ChannelWriter<Action> _writer;
    private readonly ChannelReader<Action> _reader;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts;

    internal Action<(List<long>, IUnit)>? OnUnitEnterBatch;
    internal Action<(List<long>, IUnit)>? OnUnitLeaveBatch;
    internal Action<(long, List<IUnit>)>? OnUnitEnterSync;
    internal Action<(long, List<UnitKey>)>? OnUnitLeaveSync;
    internal Action<(List<long>, IUnit, string, object, bool)>? OnUnitEvent;

    internal Action<IUnit, List<long>>? AfterBatchEnter;
    internal Action<IUnit, List<long>>? AfterBatchLeave;
    internal Action<long, List<IUnit>>? AfterSyncEnter;
    internal Action<long, List<UnitKey>>? AfterSyncLeave;
    internal Action<IUnit, List<long>, string, object?, bool>? AfterUnitEvent;

    private Action _onIdleCheck = () => { };

    internal void SetOnIdleCheck(Action handler)
    {
        _onIdleCheck = handler ?? (() => { });
    }

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
                _reader.WaitToReadAsync(_cts.Token).AsTask().Wait(1000);
            }
            catch { }

            while (_reader.TryRead(out var action))
            {
                try { action(); }
                catch { }
            }

            try { _onIdleCheck(); } catch { }
        }
    }
}
