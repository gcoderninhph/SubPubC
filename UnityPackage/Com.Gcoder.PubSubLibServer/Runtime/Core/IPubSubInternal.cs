using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

internal interface IPubSubInternal
{
    void OnUnitPositionChanged(Unit unit);
    void OnUnitDestroyed(Unit unit);
    void PublishEvent(Unit unit, string eventName, object? data, bool reliable);
}
}
