using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

internal class Unit : IUnit
{
    private Vector2 _position;
    private string _currentCellId = "";
    private IAlive? _target;
    private byte[]? _data;
    private int _version;
    internal IPubSubInternal? PubSub;

    public long Id { get; }
    public string Type { get; }

    public Vector2 Position
    {
        get => _position;
        set
        {
            if (_position.x == value.x && _position.y == value.y) return;
            _position = value;
            _version++;
            PubSub?.OnUnitPositionChanged(this);
        }
    }

    public bool IsAlive => _target?.IsAlive ?? false;

    public int Version => _version;

    public byte[]? Data { get => _data; set { _data = value; _version++; } }

    public object? Target => _target;

    void IUnit.PublishEvent(string eventName, object? data, bool reliable)
    {
        PubSub?.PublishEvent(this, eventName, data, reliable);
    }

    public void Destroy()
    {
        PubSub?.OnUnitDestroyed(this);
    }

    public string CurrentCellId
    {
        get => _currentCellId;
        set => _currentCellId = value;
    }

    internal Unit(long id, string type, Vector2 position, IAlive target)
    {
        Id = id;
        Type = type;
        _position = position;
        _target = target;
    }
}
}
