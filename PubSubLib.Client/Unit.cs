namespace PubSubLib.Client;

internal sealed class Unit : IUnit
{
    private readonly long _id;
    private readonly string _unitType;
    private readonly int _version;
    private readonly System.WeakReference _weakTarget;

    public Unit(long id, int version, string unitType, object target)
    {
        _id = id;
        _version = version;
        _unitType = unitType;
        _weakTarget = new System.WeakReference(target);
    }

    public string UnitType => _unitType;
    public long Id => _id;
    public int Version => _version;
    public bool IsAlive => _weakTarget.IsAlive;
    public object? Target => _weakTarget.Target;
}
