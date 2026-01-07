#nullable enable

namespace SubPubC;

public class Cell
{
    /// Key Format "x:y"
    private static DictionaryShard<string, Cell> _cells = new(4);

    private HashSet<string> _watchers = [];
    private ReaderWriterLockSlim _watchersLock = new();

    private HashSet<string> _units = [];
    private ReaderWriterLockSlim _unitsLock = new();

    public string[] Units
    {
        get
        {
            _unitsLock.EnterReadLock();
            try
            {
                return [.. _units];
            }
            finally
            {
                _unitsLock.ExitReadLock();
            }
        }
    }

    public string[] Watchers
    {
        get
        {
            _watchersLock.EnterReadLock();
            try
            {
                return [.. _watchers];
            }
            finally
            {
                _watchersLock.ExitReadLock();
            }
        }
    }


    public static void PublishUnitEnter(string unitId, string cellId)
    {
        Cell cell = Get(cellId) ?? Create(cellId);

        var Watchers = cell.Watchers;

        if (Watchers.Length > 0)
        {
            foreach (var watcherId in Watchers)
            {
                Watcher.PublishUnitEnter(watcherId, [cellId]);
            }
        }

        cell.AddUnit(unitId);
    }

    private void AddUnit(string unitId)
    {
        _unitsLock.EnterWriteLock();
        try
        {
            _units.Add(unitId);
        }
        finally
        {
            _unitsLock.ExitWriteLock();
        }
    }

    public static void PublishMove(string unitId, string oldCellId, string newCellId)
    {
        Cell oldCell = Get(oldCellId) ?? Create(oldCellId);
        Cell newCell = Get(newCellId) ?? Create(newCellId);

        string[] oldWatchers = oldCell.Watchers;
        string[] newWatchers = newCell.Watchers;

        string[] exitedWatchers = [.. oldWatchers.Except(newWatchers)];
        string[] enteredWatchers = [.. newWatchers.Except(oldWatchers)];

        foreach (var watcherId in exitedWatchers)
        {
            Watcher.PublishUnitExit(watcherId, unitId);
        }

        foreach (var watcherId in enteredWatchers)
        {
            Watcher.PublishUnitEnter(watcherId, unitId);
        }
    }


    public static Cell? Get(string cellId)
    {
        return _cells.TryGetValue(cellId, out var cell) ? cell : null;
    }

    private static Cell Create(string cellId)
    {
        if (_cells.TryGetValue(cellId, out var cell) && cell != null)
        {
            return cell;
        }

        cell = new Cell();
        _cells[cellId] = cell;
        return cell;
    }

    private static Cell[] GetCells(string[] cellIds)
    {
        List<Cell> cells = [];
        foreach (var cellId in cellIds)
        {
            if (_cells.TryGetValue(cellId, out Cell? cell) && cell != null)
            {
                cells.Add(cell);
            }
        }
        return [.. cells];
    }

    public static string[] GetAllUnityByCellIds(string[] cellIds)
    {
        Cell[] cells = GetCells(cellIds);
        HashSet<string> units = [];
        foreach (var cell in cells)
        {
            units.UnionWith(cell.Units);
        }
        return [.. units];
    }

    public static void AddWatcherAllCells(string watcherId, string[] cellIds)
    {
        Cell[] cells = GetCells(cellIds);
        foreach (var cell in cells)
        {
            cell.AddWatcher(watcherId);
        }
    }

    public static void RemoveWatcherAllCells(string watcherId, string[] cellIds)
    {
        Cell[] cells = GetCells(cellIds);
        foreach (var cell in cells)
        {
            cell.RemoveWatcher(watcherId);
        }
    }

    private void AddWatcher(string id)
    {
        _watchersLock.EnterWriteLock();
        try
        {
            _watchers.Add(id);
        }
        finally
        {
            _watchersLock.ExitWriteLock();
        }
    }

    private void RemoveWatcher(string id)
    {
        _watchersLock.EnterWriteLock();
        try
        {
            _watchers.Remove(id);
        }
        finally
        {
            _watchersLock.ExitWriteLock();
        }
    }

}
