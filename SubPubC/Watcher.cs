#nullable enable

using static SubPubC.SubC;

namespace SubPubC;

public class Watcher
{
    public static event Action<string, string[]>? OnUnitEnter;
    public static event Action<string, string[]>? OnUnitExit;
    public static event Action<string, string, string>? OnUnitEvent;

    private static DictionaryShard<string, Watcher> _watchers = new(8);

    private HashSet<string> _cells = [];
    private ReaderWriterLockSlim _lock = new();

    public string[] Cells
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return [.. _cells];
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    private static Watcher? Get(string id)
    {
        return _watchers.TryGetValue(id, out var watcher) ? watcher : null;

    }

    private static Watcher Create(string watcherId)
    {
        if (_watchers.ContainsKey(watcherId))
        {
            return _watchers[watcherId];
        }

        Watcher watcher = new();
        _watchers[watcherId] = watcher;
        return watcher;
    }


    public static Watcher Enter(string watcherId, Vec2 position, float range)
    {
        Watcher watcher = Get(watcherId) ?? Create(watcherId);

        string[] gridCells = GetAllGridCellsInRange(position, range);
        watcher.AddCells(gridCells);
        PublishUnitEnter(watcherId, gridCells);

        return watcher;
    }

    public static void Move(string watcherId, Vec2 newPosition, float range)
    {
        Watcher watcher = Get(watcherId) ?? Create(watcherId);

        string[] oldCells = watcher.Cells;
        string[] newCells = GetAllGridCellsInRange(newPosition, range);

        string[] cellsToAdd = [.. newCells.Except(oldCells)];
        string[] cellsToRemove = [.. oldCells.Except(newCells)];

        PublishUnitEnter(watcherId, cellsToAdd);
        PublishUnitExit(watcherId, cellsToRemove);

        watcher.AddCells(cellsToAdd);
        watcher.RemoveCells(cellsToRemove);

        Cell.AddWatcherAllCells(watcherId, cellsToAdd);
        Cell.RemoveWatcherAllCells(watcherId, cellsToRemove);
    }

    public static void Exit(string watcherId)
    {
        Watcher? watcher = Get(watcherId);
        if (watcher == null) return;
        string[] cellIds = watcher.Cells;
        Cell.RemoveWatcherAllCells(watcherId, cellIds);
        _watchers.Remove(watcherId);
    }

    public static void PublishUnitEnter(string watcherId, string[] cellIds)
    {
        string[] units = Cell.GetAllUnityByCellIds(cellIds);
        if (units.Length == 0) return;
        OnUnitEnter?.Invoke(watcherId, units);
    }

    public static void PublishUnitExit(string watcherId, string[] cellIds)
    {
        string[] units = Cell.GetAllUnityByCellIds(cellIds);
        if (units.Length == 0) return;
        OnUnitExit?.Invoke(watcherId, units);
    }

    public static void PublishUnitEnter(string watcherId, string unitId)
    {
        OnUnitEnter?.Invoke(watcherId, [unitId]);
    }

    public static void PublishUnitExit(string watcherId, string unitId)
    {
        OnUnitExit?.Invoke(watcherId, [unitId]);
    }

    public static void PublishUnitEvent(string watcherId, string unitId, string eventName)
    {
        OnUnitEvent?.Invoke(watcherId, unitId, eventName);
    }

    private void AddCells(IEnumerable<string> cellIds)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var cellId in cellIds)
            {
                _cells.Add(cellId);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void RemoveCells(IEnumerable<string> cellIds)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var cellId in cellIds)
            {
                _cells.Remove(cellId);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}

