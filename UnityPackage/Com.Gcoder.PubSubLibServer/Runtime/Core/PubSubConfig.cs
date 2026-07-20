using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

public class PubSubConfig
{
    public float GridSize = 100f;
    public int WatcherTimeoutSeconds = 5;
    public int WatcherCleanupIntervalSeconds = 2;
}
}
