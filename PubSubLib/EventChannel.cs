using System.Threading.Channels;

namespace PubSubLib;

internal sealed class EventChannel<T> : IDisposable where T : class
{
    private readonly Channel<InternalEvent> _channel;
    private readonly ChannelWriter<InternalEvent> _writer;
    private readonly ChannelReader<InternalEvent> _reader;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts;

    internal Action<(List<long>, IUnit<T>)>? OnUnitEnterBatch;
    internal Action<(List<long>, IUnit<T>)>? OnUnitLeaveBatch;
    internal Action<(long, List<IUnit<T>>)>? OnUnitEnterSync;
    internal Action<(long, List<UnitKey>)>? OnUnitLeaveSync;
    internal Action<(List<long>, IUnit<T>, string, object)>? OnUnitEvent;

    public EventChannel()
    {
        _channel = Channel.CreateUnbounded<InternalEvent>();
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

    public void Enqueue(InternalEvent evt)
    {
        _writer.TryWrite(evt);
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

            while (_reader.TryRead(out var evt))
            {
                try
                {
                    Dispatch(evt);
                }
                catch
                {
                }
                finally
                {
                    InternalEventPool.Return(evt);
                }
            }
        }
    }

    private void Dispatch(InternalEvent e)
    {
        switch (e.Type)
        {
            case EventType.BatchEnter: DispatchBatchEnter(e); break;
            case EventType.BatchLeave: DispatchBatchLeave(e); break;
            case EventType.SyncEnter: DispatchSyncEnter(e); break;
            case EventType.SyncLeave: DispatchSyncLeave(e); break;
            case EventType.UnitEvent: DispatchUnitEvent(e); break;
        }
    }

    private void DispatchBatchEnter(InternalEvent e)
    {
        var cb = OnUnitEnterBatch;
        if (cb == null || e.WatcherIds == null || e.WatcherIds.Length == 0) return;

        var watchIds = ListPool<long>.Rent();
        watchIds.AddRange(e.WatcherIds);
        try { cb((watchIds, (IUnit<T>)e.Unit!)); }
        finally { ListPool<long>.Return(watchIds); }
    }

    private void DispatchBatchLeave(InternalEvent e)
    {
        var cb = OnUnitLeaveBatch;
        if (cb == null || e.WatcherIds == null || e.WatcherIds.Length == 0) return;

        var watchIds = ListPool<long>.Rent();
        watchIds.AddRange(e.WatcherIds);
        try { cb((watchIds, (IUnit<T>)e.Unit!)); }
        finally { ListPool<long>.Return(watchIds); }
    }

    private void DispatchSyncEnter(InternalEvent e)
    {
        var cb = OnUnitEnterSync;
        if (cb == null || e.SyncUnits == null || e.SyncUnits.Length == 0) return;

        var units = ListPool<IUnit<T>>.Rent();
        foreach (var u in e.SyncUnits)
            units.Add((IUnit<T>)u!);
        try { cb((e.WatcherId, units)); }
        finally { ListPool<IUnit<T>>.Return(units); }
    }

    private void DispatchSyncLeave(InternalEvent e)
    {
        var cb = OnUnitLeaveSync;
        if (cb == null || e.SyncUnitIds == null || e.SyncUnitIds.Length == 0) return;

        var unitKeys = ListPool<UnitKey>.Rent();
        unitKeys.AddRange(e.SyncUnitIds);
        try { cb((e.WatcherId, unitKeys)); }
        finally { ListPool<UnitKey>.Return(unitKeys); }
    }

    private void DispatchUnitEvent(InternalEvent e)
    {
        var cb = OnUnitEvent;
        if (cb == null || e.WatcherIds == null || e.WatcherIds.Length == 0) return;

        var watchIds = ListPool<long>.Rent();
        watchIds.AddRange(e.WatcherIds);
        try { cb((watchIds, (IUnit<T>)e.Unit!, e.EventName!, e.Data!)); }
        finally { ListPool<long>.Return(watchIds); }
    }
}
