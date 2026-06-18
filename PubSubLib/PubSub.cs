using Gcoder.Collections;
using Natify;

namespace PubSubLib;

internal sealed class PubSub : IPubSub, IPubSubInternal
{
    private readonly float _config_GridSize;
    private readonly long _config_WatcherTimeoutTicks;
    private readonly int _config_WatcherCleanupInterval;
    private readonly Dictionary<UnitKey, Unit> _units;
    private readonly Dictionary<long, Watcher> _watchers;
    private readonly Dictionary<string, Cell> _cells;
    private readonly ISortedDictionary<long, long> _watcherExpirations;
    private readonly EventChannel _channel;
    private PubSubNatifySync? _natifySync;
    private DateTime _lastCleanupCheck;

    private PubSub(PubSubConfig config)
    {
        _config_GridSize = config.GridSize;
        _config_WatcherTimeoutTicks = config.WatcherTimeoutSeconds * TimeSpan.TicksPerSecond;
        _config_WatcherCleanupInterval = config.WatcherCleanupIntervalSeconds;
        _units = new Dictionary<UnitKey, Unit>();
        _watchers = new Dictionary<long, Watcher>();
        _cells = new Dictionary<string, Cell>();
        _watcherExpirations = new DictionaryScore<long, long>();
        _lastCleanupCheck = DateTime.UtcNow;
        _channel = new EventChannel();
        _channel.SetOnIdleCheck(CheckIdle);
    }

    internal static IPubSub Create(PubSubConfig config)
    {
        var p = new PubSub(config);
        p._channel.Start();
        return p;
    }

    // ===== IPubSub =====

    public void CreateUnit<T>(long id, string type, Vector2 position, T target, Action<IUnit> onCreated, byte[]? data = null) where T : class
    {
        _channel.Enqueue(() =>
        {
            var unit = new Unit(id, type, position, target);
            if (data != null) unit.Data = data;
            AddUnitInternal(unit);
            onCreated?.Invoke(unit);
        });
    }

    public Task<IUnit> CreateUnitAsync<T>(long id, string type, Vector2 position, T target, byte[]? data = null) where T : class
    {
        var tcs = new TaskCompletionSource<IUnit>();
        _channel.Enqueue(() =>
        {
            try
            {
                var unit = new Unit(id, type, position, target);
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

            var nowTicks = DateTime.UtcNow.Ticks;
            _watcherExpirations.Add(watcherId, nowTicks + _config_WatcherTimeoutTicks);

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

    public void WatcherPingUnits(long watcherId, Dictionary<string, Dictionary<long, int>> typeVersions)
    {
        _channel.Enqueue(() =>
        {
            if (!_watchers.TryGetValue(watcherId, out var watcher)) return;

            var nowTicks = DateTime.UtcNow.Ticks;
            _watcherExpirations.Remove(watcherId);
            _watcherExpirations.Add(watcherId, nowTicks + _config_WatcherTimeoutTicks);

            foreach (var type in typeVersions.Keys)
                watcher.RegisterKnownType(type);

            var cells = watcher.Cells;
            foreach (var type in watcher.KnownTypes)
            {
                var unitVersions = typeVersions.TryGetValue(type, out var dict) ? dict : new Dictionary<long, int>();

                var actualKeys = new HashSet<UnitKey>();
                foreach (var cellId in cells)
                {
                    if (_cells.TryGetValue(cellId, out var cell) && cell != null)
                    {
                        foreach (var uk in cell.Units)
                        {
                            if (uk.Type != type) continue;
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
                        if (!unitVersions.TryGetValue(key.Id, out var clientVersion))
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
                        var syncUnits = ListPool<IUnit>.Rent();
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
                            ListPool<IUnit>.Return(syncUnits);
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
                    foreach (var (unitId, _) in unitVersions)
                    {
                        var key = new UnitKey(unitId, type);
                        if (!actualKeys.Contains(key))
                            extraKeys.Add(key);
                    }

                    if (extraKeys.Count > 0)
                        FireSyncLeave(watcherId, extraKeys.ToArray());
                }
                finally
                {
                    ListPool<UnitKey>.Return(extraKeys);
                }
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
        _natifySync = new PubSubNatifySync(adapter, this);
        _channel.AfterBatchEnter = _natifySync.OnBatchEnter;
        _channel.AfterBatchLeave = _natifySync.OnBatchLeave;
        _channel.AfterSyncEnter = _natifySync.OnSyncEnter;
        _channel.AfterSyncLeave = _natifySync.OnSyncLeave;
        _channel.AfterUnitEvent = _natifySync.OnUnitEvent;
    }

    internal void HandleNatifyPublishEvent(long unitId, string unitType, string eventName, byte[]? data, bool reliable)
    {
        _channel.Enqueue(() =>
        {
            var key = new UnitKey(unitId, unitType);
            var unit = TryResolveAlive(key);
            if (unit != null)
            {
                var watcherIds = GetWatchersInCell(unit.CurrentCellId);
                if (watcherIds.Length > 0)
                    FireUnitEvent(watcherIds, unit, eventName, data, reliable);
            }
        });
    }

    public void OnUnitEnter(Action<(List<long> notyWatchIds, IUnit units)> callBack)
    {
        _channel.OnUnitEnterBatch = callBack;
    }

    public void OnUnitLeave(Action<(List<long> notyWatchIds, IUnit units)> callBack)
    {
        _channel.OnUnitLeaveBatch = callBack;
    }

    public void OnUnitEnter(Action<(long notyWatchId, List<IUnit> units)> callBack)
    {
        _channel.OnUnitEnterSync = callBack;
    }

    public void OnUnitLeave(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack)
    {
        _channel.OnUnitLeaveSync = callBack;
    }

    public void OnUnitEvent(Action<(List<long> notyWatchId, IUnit units, string eventName, object data, bool reliable)> callBack)
    {
        _channel.OnUnitEvent = callBack;
    }

    // ===== IPubSubInternal =====

    void IPubSubInternal.OnUnitPositionChanged(Unit unit)
    {
        _channel.Enqueue(() => HandlePositionChanged(unit));
    }

    void IPubSubInternal.OnUnitDestroyed(Unit unit)
    {
        _channel.Enqueue(() => RemoveUnitInternal(unit));
    }

    void IPubSubInternal.PublishEvent(Unit unit, string eventName, object? data, bool reliable)
    {
        _channel.Enqueue(() =>
        {
            var watcherIds = GetWatchersInCell(unit.CurrentCellId);
            if (watcherIds.Length > 0)
                FireUnitEvent(watcherIds, unit, eventName, data, reliable);
        });
    }

    internal Unit? TryResolveAlive(UnitKey key)
    {
        if (_units.TryGetValue(key, out var unit) && unit != null)
        {
            if (unit.IsAlive)
                return unit;
            RemoveUnitInternal(unit);
        }
        return null;
    }

    // ===== Expiration =====

    private void CheckIdle()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCleanupCheck).TotalSeconds >= _config_WatcherCleanupInterval)
        {
            _lastCleanupCheck = now;
            CleanupExpiredWatchers();
        }
    }

    private void CleanupExpiredWatchers()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        foreach (var (watcherId, _) in _watcherExpirations.RangeByScore(0, nowTicks))
            RemoveWatcherInternal(watcherId);
        _watcherExpirations.RemoveRangeByScore(0, nowTicks);
    }

    // ===== IDisposable =====

    public void Dispose()
    {
        _natifySync?.Dispose();
        _channel.Dispose();
    }

    // ===== Private helpers =====

    private void AddUnitInternal(IUnit unit)
    {
        var u = (Unit)unit;
        u.PubSub = this;

        var key = new UnitKey(u.Id, u.Type);
        _units[key] = u;

        var cellId = SubC.GetGridCellByPosition(u.Position, _config_GridSize);
        u.CurrentCellId = cellId;

        var cell = GetOrCreateCell(cellId);
        cell.AddUnit(key);

        var watcherIds = cell.Watchers;
        if (watcherIds.Length > 0)
            FireBatchEnter(watcherIds, u);
    }

    private void RemoveUnitInternal(IUnit unit)
    {
        var u = (Unit)unit;
        var key = new UnitKey(u.Id, u.Type);

        if (!string.IsNullOrEmpty(u.CurrentCellId))
        {
            if (_cells.TryGetValue(u.CurrentCellId, out var cell) && cell != null)
            {
                var watcherIds = cell.Watchers;
                cell.RemoveUnit(key);

                if (watcherIds.Length > 0)
                    FireBatchLeave(watcherIds, u);
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
        _watcherExpirations.Remove(watcherId);
    }

    private void HandlePositionChanged(Unit unit)
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
            FireBatchLeave(exitedWatchers, unit);

        if (enteredWatchers.Length > 0)
            FireBatchEnter(enteredWatchers, unit);
    }

    private void ResolveAndFireSyncEnter(long watcherId, UnitKey[] unitKeys)
    {
        var units = ListPool<IUnit>.Rent();
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
            ListPool<IUnit>.Return(units);
        }
    }

    // ===== Fire* helpers =====

    private void FireBatchEnter(long[] watcherIds, IUnit unit)
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

    private void FireBatchLeave(long[] watcherIds, IUnit unit)
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

    private void FireSyncEnter(long watcherId, List<IUnit> units)
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

    private void FireUnitEvent(long[] watcherIds, IUnit unit, string eventName, object? data, bool reliable)
    {
        if (watcherIds.Length == 0) return;
        var cb = _channel.OnUnitEvent;
        var after = _channel.AfterUnitEvent;
        if (cb == null && after == null) return;

        var list = ListPool<long>.Rent();
        list.AddRange(watcherIds);
        try
        {
            if (cb != null) { try { cb.Invoke((list, unit, eventName, data!, reliable)); } catch { } }
            if (after != null) { try { after.Invoke(unit, list, eventName, data, reliable); } catch { } }
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
