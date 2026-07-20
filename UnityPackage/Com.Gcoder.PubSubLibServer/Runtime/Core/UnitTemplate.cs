using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

public class UnitTemplate : IUnit
{
    public event Action<byte[]>? Init;
    private bool _isCommited;
    public long Id { get; }
    public string Type { get; }
    public Vector2 Position { get; set; }
    public bool IsAlive { get; }
    public object? Target { get; }
    public int Version { get; }

    public byte[]? Data
    {
        get => null;
        set
        {
            if (_isCommited) return;
            _isCommited = true;
            if (value != null && Init != null)
            {
                Init.Invoke(value);
            }
        }
    }

    public void PublishEvent(string eventName, object? data, bool reliable = true)
    {
    }

    public void Destroy()
    {
    }
}
}
