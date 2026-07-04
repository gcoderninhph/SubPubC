namespace PubSubLib;

internal interface IRegionUnitInternal
{
    void SetUnit(IUnit unit);
    string GetUnitType();
    IUnit GetUnit();
}
