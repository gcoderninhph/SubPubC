namespace PubSubLib.Client;

public interface IPubSubClient
{
    void Tick();
    void MoveWatcher(Vector2 postion, float radius);
    IPubSubClient AddProvider(IProvider provider);
    IReadOnlyList<IUnit> GetAllUnits();
    IReadOnlyList<IUnit> GetAllUnitsByType(string unitType);
    IUnit? GetUnit(long unitId, string unitType);
}