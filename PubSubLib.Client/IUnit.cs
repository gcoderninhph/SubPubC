namespace PubSubLib.Client;

public interface IUnit
{
    string UnitType { get; }
    long Id { get; }
    int Version { get; }
    bool IsAlive { get; }
}