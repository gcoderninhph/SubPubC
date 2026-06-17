using Natify;

namespace PubSubLib;

internal sealed class PubSub<T> : IPubSub, IPubSubInternal where T : class
{
    private readonly float _config_GridSize;
    private readonly Dictionary<UnitKey, Unit<T>> _units;
    private readonly Dictionary<long, Watcher> _watchers;
    private readonly Dictionary<string, Cell> _cells;
    private readonly EventChannel<T> _channel;
    private PubSubNatifySync<T>? _natifySync;

    private PubSub(PubSubConfig config)
    {
        _config_GridSize = config.GridSize;
        _units = new Dictionary<UnitKey, Unit<T>>();
        _watchers = new Dictionary<long, Watcher>();
        _cells = new Dictionary<string, Cell>();
        _channel = new EventChannel<T>();
    }

    internal static IPubSub Create(PubSubConfig config)
    {
        var p = new PubSub<T>(config);
        p._channel.Start();
        return p;
    }

    // ===== IPubSub =====

    public void CreateUnit<TUnit>(long id, string type, Vector2 position, TUnit target, Action<IUnit<TUnit>> onCreated, byte[]? data = null) where TUnit : class
    {
        _channel.Enqueue(() =>
        {
            var unit = new Unit<TUnit>(id, type, position, new WeakReference<TUnit>(target));
            if (data != null) unit.Data = data;
            AddUnitInternal(unit);
            onCreated?.Invoke(unit);
        });
    }

    public Task<IUnit<TUnit>> CreateUnitAsync<TUnit>(long id, string type, Vector2 position, TUnit target, byte[]? data = null) where TUnit : class
    {
        var tcs = new TaskCompletionSource<IUnit<TUnit>>();
        _channel.Enqueue(() =>
        {
            try
            {
                var unit = new Unit<TUnit>(id, type, position, new WeakReference<TUnit>(target));
                if (data != null) unit.Data = data;
                AddUnitInternal(unit);
                tcs.SetResult(unit);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task FlushAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        _channel.Enqueue(() => tcs.SetResult(true));
        return tcs.Task;
    }

    public void AddWatcher(long watcherId, Vector2 position, float radius)
    {
        _channel.Enqueue(() =>
        {
            if (_watchers.TryGetValue(watcherId, out _))
                RemoveWatcherInternal(watcherId);

            var watcher = new Watcher(watcherId, position, radius);
            _watchers[watcherId] = watcher;

            var cellIds = SubC.GetAllGridCellsInRange(position, radius, _config_GridSize);
            watcher.AddCells(cellIds);

            foreach (var cellId in cellIds)
                GetOrCreateCell(cellId).AddWatcher(watcherId);

            var allUnitIds = GetAllUnitsInCells(cellIds);
            if (allUnitIds.Length > 0)
                ResolveAndFireSyncEnter(watcherId, allUnitIds);
        });
    }

    public void RemoveWatcher(long watcherId)
    {
        _channel.Enqueue(() => RemoveWatcherInternal(watcherId));
    }

    public void MoveWatcher(long watcherId, Vector2 position, float radius)
    {
        _channel.Enqueue(() =>
        {
            if (!_watchers.TryGetValue(watcherId, out var watcher)) return;

            var oldCells = watcher.Cells;
            var newCells = SubC.GetAllGridCellsInRange(position, radius, _config_GridSize);

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
                    ResolveAndFireSyncEnter(watcherId, addedUnitIds);
            }

            if (cellsToRemove.Length > 0)
            {
                var removedUnitIds = GetAllUnitsInCells(cellsToRemove);
                if (removedUnitIds.Length > 0)
                    FireSyncLeave(watcherId, removedUnitIds);
            }
        });
    }

    public void WatcherPingUnits(long watcherId, string unitType, Dictionary<UnitKey, int> unitVersions)
    {
        _channel.Enqueue(() =>
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

            var syncKeys = ListPool<UnitKey>.Rent();
            try
            {
                foreach (var key in actualKeys)
                {
                    if (!unitVersions.TryGetValue(key, out var clientVersion))
                    {
                        syncKeys.Add(key);
                    }
                    else
                    {
                        var unit = TryResolveAlive(key);
                        if (unit != null && unit.Version != clientVersion)
                            syncKeys.Add(key);
                    }
                }

                if (syncKeys.Count > 0)
                {
                    var syncUnits = ListPool<IUnit<T>>.Rent();
                    try
                    {
                        foreach (var key in syncKeys)
                        {
                            var unit = TryResolveAlive(key);
                            if (unit != null)
                                syncUnits.Add(unit);
                        }

                        if (syncUnits.Count > 0)
                            FireSyncEnter(watcherId, syncUnits);
                    }
                    finally
                    {
                        ListPool<IUnit<T>>.Return(syncUnits);
                    }
                }
            }
            finally
            {
                ListPool<UnitKey>.Return(syncKeys);
            }

            var extraKeys = ListPool<UnitKey>.Rent();
            try
            {
                foreach (var kvp in unitVersions)
                {
                    if (!actualKeys.Contains(kvp.Key))
                        extraKeys.Add(kvp.Key);
                }

                if (extraKeys.Count > 0)
                    FireSyncLeave(watcherId, extraKeys.ToArray());
            }
            finally
            {
                ListPool<UnitKey>.Return(extraKeys);
            }
        });
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

    internal void HandleNatifyPublishEvent(long unitId, string unitType, string eventName, byte[]? data)
    {
        _channel.Enqueue(() =>
        {
            var key = new UnitKey(unitId, unitType);
            var unit = TryResolveAlive(key);
            if (unit != null)
            {
                var watcherIds = GetWatchersInCell(unit.CurrentCellId);
                if (watcherIds.Length > 0)
                    FireUnitEvent(watcherIds, unit, eventName, data);
            }
        });
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
        _channel.Enqueue(() => HandlePositionChanged(unit));
    }

    void IPubSubInternal.OnUnitDestroyed<TUnit>(Unit<TUnit> unit)
    {
        _channel.Enqueue(() => RemoveUnitInternal(unit));
    }

    void IPubSubInternal.PublishEvent<TUnit>(Unit<TUnit> unit, string eventName, object? data)
    {
        _channel.Enqueue(() =>
        {
            var watcherIds = GetWatchersInCell(unit.CurrentCellId);
            if (watcherIds.Length > 0)
                FireUnitEvent(watcherIds, (IUnit<T>)(object)unit, eventName, data);
        });
    }

    internal Unit<T>? TryResolveAlive(UnitKey key)
    {
        if (_units.TryGetValue(key, out var unit) && unit != null)
        {
            if (unit.IsAlive)
                return unit;
            RemoveUnitInternal<T>(unit);
        }
        return null;
    }

    // ===== IDisposable =====

    public void Dispose()
    {
        _natifySync?.Dispose();
        _channel.Dispose();
    }

    // ===== Private helpers =====

    private void AddUnitInternal<TUnit>(IUnit<TUnit> unit) where TUnit : class
    {
        var u = (Unit<TUnit>)unit;
        u.PubSub = this;

        var key = new UnitKey(u.Id, u.Type);
        _units[key] = (Unit<T>)(object)u;

        var cellId = SubC.GetGridCellByPosition(u.Position, _config_GridSize);
        u.CurrentCellId = cellId;

        var cell = GetOrCreateCell(cellId);
        cell.AddUnit(key);

        var watcherIds = cell.Watchers;
        if (watcherIds.Length > 0)
            FireBatchEnter(watcherIds, (IUnit<T>)(object)u);
    }

    private void RemoveUnitInternal<TUnit>(IUnit<TUnit> unit) where TUnit : class
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
                    FireBatchLeave(watcherIds, (IUnit<T>)(object)u);
            }
        }

        u.CurrentCellId = "";
        u.PubSub = null;
        _units.Remove(key);
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
            FireSyncLeave(watcherId, allUnitIds);
    }

    private void HandlePositionChanged<TUnit>(Unit<TUnit> unit) where TUnit : class
    {
        var oldCellId = unit.CurrentCellId;
        var newCellId = SubC.GetGridCellByPosition(unit.Position, _config_GridSize);

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
            FireBatchLeave(exitedWatchers, (IUnit<T>)(object)unit);

        if (enteredWatchers.Length > 0)
            FireBatchEnter(enteredWatchers, (IUnit<T>)(object)unit);
    }

    private void ResolveAndFireSyncEnter(long watcherId, UnitKey[] unitKeys)
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
                FireSyncEnter(watcherId, units);
        }
        finally
        {
            ListPool<IUnit<T>>.Return(units);
        }
    }

    // ===== Fire* helpers — invoke callbacks directly (called from worker thread) =====

    private void FireBatchEnter(long[] watcherIds, IUnit<T> unit)
    {
        if (watcherIds.Length == 0) return;
        var cb = _channel.OnUnitEnterBatch;
        var after = _channel.AfterBatchEnter;
        if (cb == null && after == null) return;

        var list = ListPool<long>.Rent();
        list.AddRange(watcherIds);
        try
        {
            if (cb != null) { try { cb.Invoke((list, unit)); } catch { } }
            if (after != null) { try { after.Invoke(unit, list); } catch { } }
        }
        finally { ListPool<long>.Return(list); }
    }

    private void FireBatchLeave(long[] watcherIds, IUnit<T> unit)
    {
        if (watcherIds.Length == 0) return;
        var cb = _channel.OnUnitLeaveBatch;
        var after = _channel.AfterBatchLeave;
        if (cb == null && after == null) return;

        var list = ListPool<long>.Rent();
        list.AddRange(watcherIds);
        try
        {
            if (cb != null) { try { cb.Invoke((list, unit)); } catch { } }
            if (after != null) { try { after.Invoke(unit, list); } catch { } }
        }
        finally { ListPool<long>.Return(list); }
    }

    private void FireSyncEnter(long watcherId, List<IUnit<T>> units)
    {
        if (units.Count == 0) return;
        var cb = _channel.OnUnitEnterSync;
        var after = _channel.AfterSyncEnter;
        if (cb == null && after == null) return;
        if (cb != null) { try { cb.Invoke((watcherId, units)); } catch { } }
        if (after != null) { try { after.Invoke(watcherId, units); } catch { } }
    }

    private void FireSyncLeave(long watcherId, UnitKey[] unitKeys)
    {
        if (unitKeys.Length == 0) return;
        var cb = _channel.OnUnitLeaveSync;
        var after = _channel.AfterSyncLeave;
        if (cb == null && after == null) return;

        var list = ListPool<UnitKey>.Rent();
        list.AddRange(unitKeys);
        try
        {
            if (cb != null) { try { cb.Invoke((watcherId, list)); } catch { } }
            if (after != null) { try { after.Invoke(watcherId, list); } catch { } }
        }
        finally { ListPool<UnitKey>.Return(list); }
    }

    private void FireUnitEvent(long[] watcherIds, IUnit<T> unit, string eventName, object? data)
    {
        if (watcherIds.Length == 0) return;
        var cb = _channel.OnUnitEvent;
        var after = _channel.AfterUnitEvent;
        if (cb == null && after == null) return;

        var list = ListPool<long>.Rent();
        list.AddRange(watcherIds);
        try
        {
            if (cb != null) { try { cb.Invoke((list, unit, eventName, data!)); } catch { } }
            if (after != null) { try { after.Invoke(unit, list, eventName, data); } catch { } }
        }
        finally { ListPool<long>.Return(list); }
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
}
