namespace PubSubLib.Client;

public interface IUnit
{
    string UnitType { get; }
    long Id { get; }
    int Version { get; }
    bool IsAlive { get; }
    object? Target { get; }
}

public interface IUnit<T> : IUnit where T : class, IAlive
{
    new T? Target { get; }
}