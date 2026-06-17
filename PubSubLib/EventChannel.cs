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

    internal Action<IUnit<T>, List<long>>? AfterBatchEnter;
    internal Action<IUnit<T>, List<long>>? AfterBatchLeave;
    internal Action<long, List<IUnit<T>>>? AfterSyncEnter;
    internal Action<long, List<UnitKey>>? AfterSyncLeave;
    internal Action<IUnit<T>, List<long>, string, object?>? AfterUnitEvent;

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
        if (e.WatcherIds == null || e.WatcherIds.Length == 0) return;

        var cb = OnUnitEnterBatch;
        var after = AfterBatchEnter;
        if (cb == null && after == null) return;

        var watchIds = ListPool<long>.Rent();
        watchIds.AddRange(e.WatcherIds);
        try
        {
            cb?.Invoke((watchIds, (IUnit<T>)e.Unit!));
            after?.Invoke((IUnit<T>)e.Unit!, watchIds);
        }
        finally { ListPool<long>.Return(watchIds); }
    }

    private void DispatchBatchLeave(InternalEvent e)
    {
        if (e.WatcherIds == null || e.WatcherIds.Length == 0) return;

        var cb = OnUnitLeaveBatch;
        var after = AfterBatchLeave;
        if (cb == null && after == null) return;

        var watchIds = ListPool<long>.Rent();
        watchIds.AddRange(e.WatcherIds);
        try
        {
            cb?.Invoke((watchIds, (IUnit<T>)e.Unit!));
            after?.Invoke((IUnit<T>)e.Unit!, watchIds);
        }
        finally { ListPool<long>.Return(watchIds); }
    }

    private void DispatchSyncEnter(InternalEvent e)
    {
        if (e.SyncUnits == null || e.SyncUnits.Length == 0) return;

        var cb = OnUnitEnterSync;
        var after = AfterSyncEnter;
        if (cb == null && after == null) return;

        var units = ListPool<IUnit<T>>.Rent();
        foreach (var u in e.SyncUnits)
            units.Add((IUnit<T>)u!);
        try
        {
            cb?.Invoke((e.WatcherId, units));
            after?.Invoke(e.WatcherId, units);
        }
        finally { ListPool<IUnit<T>>.Return(units); }
    }

    private void DispatchSyncLeave(InternalEvent e)
    {
        if (e.SyncUnitIds == null || e.SyncUnitIds.Length == 0) return;

        var cb = OnUnitLeaveSync;
        var after = AfterSyncLeave;
        if (cb == null && after == null) return;

        var unitKeys = ListPool<UnitKey>.Rent();
        unitKeys.AddRange(e.SyncUnitIds);
        try
        {
            cb?.Invoke((e.WatcherId, unitKeys));
            after?.Invoke(e.WatcherId, unitKeys);
        }
        finally { ListPool<UnitKey>.Return(unitKeys); }
    }

    private void DispatchUnitEvent(InternalEvent e)
    {
        if (e.WatcherIds == null || e.WatcherIds.Length == 0) return;

        var cb = OnUnitEvent;
        var after = AfterUnitEvent;
        if (cb == null && after == null) return;

        var watchIds = ListPool<long>.Rent();
        watchIds.AddRange(e.WatcherIds);
        try
        {
            cb?.Invoke((watchIds, (IUnit<T>)e.Unit!, e.EventName!, e.Data!));
            after?.Invoke((IUnit<T>)e.Unit!, watchIds, e.EventName!, e.Data);
        }
        finally { ListPool<long>.Return(watchIds); }
    }
}
