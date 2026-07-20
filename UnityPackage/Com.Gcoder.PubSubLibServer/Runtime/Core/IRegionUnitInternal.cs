namespace PubSubLib
{
    public interface IRegionUnitInternal
    {
        void SetUnit(IUnit unit);
        string GetUnitType();
        IUnit GetUnit();
    }
}