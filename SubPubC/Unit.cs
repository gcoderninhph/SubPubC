#nullable enable

namespace SubPubC;

public class Unit
{
    private static DictionaryShard<string, Unit> _units = new(16);
    private string currentCellId = string.Empty;

    public static Unit Enter(string unitId, Vec2 position)
    {
        Exit(unitId);
        Unit unit = Create(unitId);
        unit.currentCellId = SubC.GetGridCellByPosition(position);
        Cell.PublishUnitEnter(unitId, unit.currentCellId);
        return unit;
    }

    private static Unit? Get(string unitId)
    {
        if (_units.TryGetValue(unitId, out Unit? unit)) return unit;
        return null;
    }

    private static Unit Create(string unitId)
    {
        if (_units.ContainsKey(unitId))
        {
            return _units[unitId];
        }

        Unit unit = new();
        _units[unitId] = unit;
        return unit;
    }

    public static void Move(string unitId, Vec2 newPosition)
    {
        Unit unit = Get(unitId) ?? Create(unitId);

        if (unit.currentCellId != string.Empty)
        {
            string newCellId = SubC.GetGridCellByPosition(newPosition);
            if (unit.currentCellId != newCellId)
            {
                Cell.PublishMove(unitId, unit.currentCellId, newCellId);
            }
            unit.currentCellId = newCellId;
        }
        else
        {
            unit.currentCellId = SubC.GetGridCellByPosition(newPosition);
            Cell.PublishUnitEnter(unitId, unit.currentCellId);
        }
    }

    public static void Exit(string unitId)
    {
        Unit? unit = Get(unitId);
        if (unit == null) return;

        if (unit.currentCellId != string.Empty)
        {
            var cell = Cell.Get(unit.currentCellId);
            if (cell != null)
            {
                foreach (var watcherId in cell.Watchers)
                {
                    Watcher.PublishUnitExit(watcherId, unitId);
                }

                cell.RemoveUnit(unitId);
            }
        }
        unit.currentCellId = string.Empty;
        _units.Remove(unitId);
    }

    public static void Event(string unitId, string eventName)
    {
        Unit? unit = Get(unitId);
        if (unit == null) return;

        if (unit.currentCellId != string.Empty)
        {
            Cell? cell = Cell.Get(unit.currentCellId);
            if (cell == null) return;

            foreach (var watcherId in cell.Watchers)
            {
                Watcher.PublishUnitEvent(watcherId, unitId, eventName);
            }
        }
    }
}