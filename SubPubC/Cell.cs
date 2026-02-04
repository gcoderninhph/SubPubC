#nullable enable

namespace SubPubC;

public class Cell
{
    /// Key Format "x:y"
    private static DictionaryShard<string, Cell> _cells = new(4);

    private HashSetShard<string> _watchers = new(4);
    private HashSetShard<string> _units = new(4);

    public string[] Units
    {
        get
        {
            return _units.ToArray();
        }
    }

    public string[] Watchers
    {
        get
        {
            return _watchers.ToArray();
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
                Watcher.PublishUnitEnter(watcherId, unitId);
            }
        }

        cell.AddUnit(unitId);
    }

    public void AddUnit(string unitId)
    {
        _units.Add(unitId);
    }

    public void RemoveUnit(string unitId)
    {
        _units.Remove(unitId);
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

        oldCell.RemoveUnit(unitId);
        newCell.AddUnit(unitId);
    }


    public static Cell? Get(string cellId)
    {
        return _cells.TryGetValue(cellId, out var cell) ? cell : null;
    }

    public static Cell Create(string cellId)
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

    public void AddWatcher(string id)
    {
        _watchers.Add(id);
    }

    private void RemoveWatcher(string id)
    {
        _watchers.Remove(id);
    }

}
