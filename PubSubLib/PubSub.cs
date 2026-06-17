using Natify;

namespace PubSubLib;

internal class PubSub<T> : IPubSub, IPubSubInternal where T : class
{
    private readonly PubSubConfig _config;
    private readonly EventChannel<T> _channel;
    private readonly Dictionary<UnitKey, Unit<T>> _units;
    private readonly Dictionary<long, Watcher> _watchers;
    private readonly Dictionary<string, Cell> _cells;
    private PubSubNatifySync<T>? _natifySync;

    private PubSub(PubSubConfig config)
    {
        _config = config;
        _channel = new EventChannel<T>();
        _units = new Dictionary<UnitKey, Unit<T>>();
        _watchers = new Dictionary<long, Watcher>();
        _cells = new Dictionary<string, Cell>();
    }

    internal static IPubSub Create(PubSubConfig config)
    {
        var p = new PubSub<T>(config);
        p._channel.Start();
        return p;
    }

    // ===== IPubSub =====

    public void AddUnit<TUnit>(IUnit<TUnit> unit) where TUnit : class
    {
        var u = (Unit<TUnit>)unit;
        u.PubSub = this;

        var key = new UnitKey(u.Id, u.Type);
        _units[key] = (Unit<T>)(object)u;

        var cellId = SubC.GetGridCellByPosition(u.Position, _config.GridSize);
        u.CurrentCellId = cellId;

        var cell = GetOrCreateCell(cellId);
        cell.AddUnit(key);

        var watcherIds = cell.Watchers;
        if (watcherIds.Length > 0)
            EnqueueBatchEnter(watcherIds, (IUnit<T>)(object)u);
    }

    public void RemoveUnit<TUnit>(IUnit<TUnit> unit) where TUnit : class
    {
        var u = (Unit<TUnit>)unit;
        var key = new UnitKey(u.Id, u.Type);

        if (!string.IsNullOrEmpty(u.CurrentCellId))
        {
            if (_cells.TryGetValue(u.CurrentCellId, out var cell) && cell != null)
            {
                var watcherIds = cell.Watchers;
                cell.RemoveUnit(key);

                if (watcherIds.Length > 0)
                    EnqueueBatchLeave(watcherIds, (IUnit<T>)(object)u);
            }
        }

        u.CurrentCellId = "";
        u.PubSub = null;
        _units.Remove(key);
    }

    public void AddWatcher(long watcherId, Vector2 position, float radius)
    {
        if (_watchers.TryGetValue(watcherId, out _))
            RemoveWatcherInternal(watcherId);

        var watcher = new Watcher(watcherId, position, radius);
        _watchers[watcherId] = watcher;

        var cellIds = SubC.GetAllGridCellsInRange(position, radius, _config.GridSize);
        watcher.AddCells(cellIds);

        foreach (var cellId in cellIds)
            GetOrCreateCell(cellId).AddWatcher(watcherId);

        var allUnitIds = GetAllUnitsInCells(cellIds);
        if (allUnitIds.Length > 0)
            ResolveAndSyncEnter(watcherId, allUnitIds);
    }

    public void RemoveWatcher(long watcherId)
    {
        RemoveWatcherInternal(watcherId);
    }

    private void RemoveWatcherInternal(long watcherId)
    {
        if (!_watchers.TryGetValue(watcherId, out var watcher)) return;

        var cellIds = watcher.Cells;
        foreach (var cellId in cellIds)
        {
            if (_cells.TryGetValue(cellId, out var cell) && cell != null)
                cell.RemoveWatcher(watcherId);
        }

        _watchers.Remove(watcherId);

        var allUnitIds = GetAllUnitsInCells(cellIds);
        if (allUnitIds.Length > 0)
            EnqueueSyncLeave(watcherId, allUnitIds);
    }

    public void MoveWatcher(long watcherId, Vector2 position, float radius)
    {
        if (!_watchers.TryGetValue(watcherId, out var watcher)) return;

        var oldCells = watcher.Cells;
        var newCells = SubC.GetAllGridCellsInRange(position, radius, _config.GridSize);

        watcher.Position = position;
        watcher.Radius = radius;

        var cellsToAdd = newCells.Except(oldCells).ToArray();
        var cellsToRemove = oldCells.Except(newCells).ToArray();

        watcher.AddCells(cellsToAdd);
        watcher.RemoveCells(cellsToRemove);

        foreach (var cellId in cellsToAdd)
            GetOrCreateCell(cellId).AddWatcher(watcherId);
        foreach (var cellId in cellsToRemove)
        {
            if (_cells.TryGetValue(cellId, out var cell) && cell != null)
                cell.RemoveWatcher(watcherId);
        }

        if (cellsToAdd.Length > 0)
        {
            var addedUnitIds = GetAllUnitsInCells(cellsToAdd);
            if (addedUnitIds.Length > 0)
                ResolveAndSyncEnter(watcherId, addedUnitIds);
        }

        if (cellsToRemove.Length > 0)
        {
            var removedUnitIds = GetAllUnitsInCells(cellsToRemove);
            if (removedUnitIds.Length > 0)
                EnqueueSyncLeave(watcherId, removedUnitIds);
        }
    }

    public void WatcherPingUnits(long watcherId, string unitType, List<UnitKey> unitKeys)
    {
        if (!_watchers.TryGetValue(watcherId, out var watcher)) return;

        var cells = watcher.Cells;
        var actualKeys = new HashSet<UnitKey>();
        foreach (var cellId in cells)
        {
            if (_cells.TryGetValue(cellId, out var cell) && cell != null)
            {
                foreach (var uk in cell.Units)
                {
                    if (uk.Type != unitType) continue;
                    var unit = TryResolveAlive(uk);
                    if (unit != null)
                        actualKeys.Add(uk);
                }
            }
        }

        var missingKeys = ListPool<UnitKey>.Rent();
        try
        {
            foreach (var key in actualKeys)
            {
                if (!unitKeys.Contains(key))
                    missingKeys.Add(key);
            }

            if (missingKeys.Count > 0)
            {
                var missingUnits = ListPool<IUnit<T>>.Rent();
                try
                {
                    foreach (var key in missingKeys)
                    {
                        var unit = TryResolveAlive(key);
                        if (unit != null)
                            missingUnits.Add(unit);
                    }

                    if (missingUnits.Count > 0)
                        EnqueueSyncEnter(watcherId, missingUnits);
                }
                finally
                {
                    ListPool<IUnit<T>>.Return(missingUnits);
                }
            }
        }
        finally
        {
            ListPool<UnitKey>.Return(missingKeys);
        }

        var extraKeys = ListPool<UnitKey>.Rent();
        try
        {
            foreach (var key in unitKeys)
            {
                if (!actualKeys.Contains(key))
                    extraKeys.Add(key);
            }

            if (extraKeys.Count > 0)
                EnqueueSyncLeave(watcherId, extraKeys.ToArray());
        }
        finally
        {
            ListPool<UnitKey>.Return(extraKeys);
        }
    }

    public void AddNatify(NatifyClientFast client)
    {
        var adapter = new NatifyAdapter(client);
        AddNatifyInternal(adapter);
    }

    public void AddNatify(NatifyClient client)
    {
        var adapter = new NatifyAdapter(client);
        AddNatifyInternal(adapter);
    }

    internal void AddNatifyInternal(INatifyAdapter adapter)
    {
        _natifySync?.Dispose();
        _natifySync = new PubSubNatifySync<T>(adapter, this);
        _channel.AfterBatchEnter = _natifySync.OnBatchEnter;
        _channel.AfterBatchLeave = _natifySync.OnBatchLeave;
        _channel.AfterSyncEnter = _natifySync.OnSyncEnter;
        _channel.AfterSyncLeave = _natifySync.OnSyncLeave;
        _channel.AfterUnitEvent = _natifySync.OnUnitEvent;
    }

    void IPubSubInternal.PublishEvent<TUnit>(Unit<TUnit> unit, string eventName, object? data)
    {
        var cellId = SubC.GetGridCellByPosition(unit.Position, _config.GridSize);
        var watcherIds = GetWatchersInCell(cellId);

        if (watcherIds.Length > 0)
        {
            var evt = InternalEventPool.Rent();
            evt.Type = EventType.UnitEvent;
            evt.WatcherIds = watcherIds;
            evt.Unit = unit;
            evt.EventName = eventName;
            evt.Data = data;
            _channel.Enqueue(evt);
        }
    }

    public void OnUnitEnter<TUnit>(Action<(List<long> notyWatchIds, IUnit<TUnit> units)> callBack) where TUnit : class
    {
        _channel.OnUnitEnterBatch = (Action<(List<long>, IUnit<T>)>)(object)callBack;
    }

    public void OnUnitLeave<TUnit>(Action<(List<long> notyWatchIds, IUnit<TUnit> units)> callBack) where TUnit : class
    {
        _channel.OnUnitLeaveBatch = (Action<(List<long>, IUnit<T>)>)(object)callBack;
    }

    public void OnUnitEnter<TUnit>(Action<(long notyWatchId, List<IUnit<TUnit>> units)> callBack) where TUnit : class
    {
        _channel.OnUnitEnterSync = (Action<(long, List<IUnit<T>>)>)(object)callBack;
    }

    public void OnUnitLeave<TUnit>(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack) where TUnit : class
    {
        _channel.OnUnitLeaveSync = callBack;
    }

    public void OnUnitEvent<TUnit>(Action<(List<long> notyWatchId, IUnit<TUnit> units, string eventName, object data)> callBack) where TUnit : class
    {
        _channel.OnUnitEvent = (Action<(List<long>, IUnit<T>, string, object)>)(object)callBack;
    }

    // ===== IPubSubInternal =====

    void IPubSubInternal.OnUnitPositionChanged<TUnit>(Unit<TUnit> unit)
    {
        var oldCellId = unit.CurrentCellId;
        var newCellId = SubC.GetGridCellByPosition(unit.Position, _config.GridSize);

        if (oldCellId == newCellId) return;

        unit.CurrentCellId = newCellId;

        var oldWatchers = !string.IsNullOrEmpty(oldCellId) ? GetWatchersInCell(oldCellId) : Array.Empty<long>();
        var newWatchers = GetWatchersInCell(newCellId);

        var exitedWatchers = oldWatchers.Except(newWatchers).ToArray();
        var enteredWatchers = newWatchers.Except(oldWatchers).ToArray();

        var key = new UnitKey(unit.Id, unit.Type);

        if (!string.IsNullOrEmpty(oldCellId))
        {
            if (_cells.TryGetValue(oldCellId, out var oldCell) && oldCell != null)
                oldCell.RemoveUnit(key);
        }

        var newCell = GetOrCreateCell(newCellId);
        newCell.AddUnit(key);

        if (exitedWatchers.Length > 0)
        {
            var evt = InternalEventPool.Rent();
            evt.Type = EventType.BatchLeave;
            evt.WatcherIds = exitedWatchers;
            evt.Unit = unit;
            _channel.Enqueue(evt);
        }

        if (enteredWatchers.Length > 0)
        {
            var evt = InternalEventPool.Rent();
            evt.Type = EventType.BatchEnter;
            evt.WatcherIds = enteredWatchers;
            evt.Unit = unit;
            _channel.Enqueue(evt);
        }
    }

    void IPubSubInternal.OnUnitDestroyed<TUnit>(Unit<TUnit> unit)
    {
        RemoveUnit<TUnit>(unit);
    }

    // ===== IDisposable =====

    public void Dispose()
    {
        _natifySync?.Dispose();
        _channel.Dispose();
    }

    // ===== Private helpers =====

    internal Unit<T>? TryResolveAlive(UnitKey key)
    {
        if (_units.TryGetValue(key, out var unit) && unit != null)
        {
            if (unit.IsAlive)
                return unit;
            RemoveUnit<T>(unit);
        }
        return null;
    }

    private Cell GetOrCreateCell(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell) && cell != null)
            return cell;
        cell = new Cell();
        _cells[cellId] = cell;
        return cell;
    }

    private long[] GetWatchersInCell(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell) && cell != null)
            return cell.Watchers;
        return Array.Empty<long>();
    }

    private UnitKey[] GetAllUnitsInCells(string[] cellIds)
    {
        var allUnits = new HashSet<UnitKey>();
        foreach (var cellId in cellIds)
        {
            if (_cells.TryGetValue(cellId, out var cell) && cell != null)
            {
                foreach (var uk in cell.Units)
                    allUnits.Add(uk);
            }
        }
        return allUnits.ToArray();
    }

    private void ResolveAndSyncEnter(long watcherId, UnitKey[] unitKeys)
    {
        var units = ListPool<IUnit<T>>.Rent();
        try
        {
            foreach (var key in unitKeys)
            {
                var unit = TryResolveAlive(key);
                if (unit != null)
                    units.Add(unit);
            }
            if (units.Count > 0)
                EnqueueSyncEnter(watcherId, units);
        }
        finally
        {
            ListPool<IUnit<T>>.Return(units);
        }
    }

    private void EnqueueBatchEnter(long[] watcherIds, IUnit<T> unit)
    {
        var evt = InternalEventPool.Rent();
        evt.Type = EventType.BatchEnter;
        evt.WatcherIds = watcherIds;
        evt.Unit = unit;
        _channel.Enqueue(evt);
    }

    private void EnqueueBatchLeave(long[] watcherIds, IUnit<T> unit)
    {
        var evt = InternalEventPool.Rent();
        evt.Type = EventType.BatchLeave;
        evt.WatcherIds = watcherIds;
        evt.Unit = unit;
        _channel.Enqueue(evt);
    }

    private void EnqueueSyncEnter(long watcherId, List<IUnit<T>> units)
    {
        var evt = InternalEventPool.Rent();
        evt.Type = EventType.SyncEnter;
        evt.WatcherId = watcherId;
        evt.SyncUnits = new object[units.Count];
        for (int i = 0; i < units.Count; i++)
            evt.SyncUnits[i] = units[i]!;
        _channel.Enqueue(evt);
    }

    private void EnqueueSyncLeave(long watcherId, UnitKey[] unitKeys)
    {
        var evt = InternalEventPool.Rent();
        evt.Type = EventType.SyncLeave;
        evt.WatcherId = watcherId;
        evt.SyncUnitIds = unitKeys;
        _channel.Enqueue(evt);
    }
}
