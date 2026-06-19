namespace PubSubLib.Client;

internal sealed class TypedUnit<T> : IUnit<T> where T : class, IAlive
{
    private readonly IUnit _inner;

    internal TypedUnit(IUnit inner) => _inner = inner;

    public string UnitType => _inner.UnitType;
    public long Id => _inner.Id;
    public int Version => _inner.Version;
    public bool IsAlive => _inner.IsAlive;

    public T? Target => (T?)_inner.Target;

    object? IUnit.Target => _inner.Target;
}
