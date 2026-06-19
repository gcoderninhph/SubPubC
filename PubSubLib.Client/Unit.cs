namespace PubSubLib.Client;

internal sealed class Unit : IUnit
{
    private readonly long _id;
    private readonly string _unitType;
    private readonly int _version;
    private readonly IAlive _target;

    public Unit(long id, int version, string unitType, IAlive target)
    {
        _id = id;
        _version = version;
        _unitType = unitType;
        _target = target;
    }

    public string UnitType => _unitType;
    public long Id => _id;
    public int Version => _version;
    public bool IsAlive => _target.IsAlive;
    public object? Target => _target;
}
