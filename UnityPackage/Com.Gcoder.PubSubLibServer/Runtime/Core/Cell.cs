using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

internal class Cell
{
    private HashSet<long> _watchers = new();
    private HashSet<UnitKey> _units = new();

    public UnitKey[] Units => _units.ToArray();
    public long[] Watchers => _watchers.ToArray();

    public void AddUnit(UnitKey unitKey) => _units.Add(unitKey);
    public void RemoveUnit(UnitKey unitKey) => _units.Remove(unitKey);
    public void AddWatcher(long watcherId) => _watchers.Add(watcherId);
    public void RemoveWatcher(long watcherId) => _watchers.Remove(watcherId);
}
}
