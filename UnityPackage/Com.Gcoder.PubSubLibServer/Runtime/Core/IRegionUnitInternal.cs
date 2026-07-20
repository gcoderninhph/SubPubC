using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

public interface IRegionUnitInternal
{
    void SetUnit(IUnit unit);
    string GetUnitType();
    IUnit GetUnit();
}
}
