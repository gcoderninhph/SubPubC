using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

public interface ISetRegionUnit<T, TR>
{
    void SetRegionUnit(T region);
}
}
