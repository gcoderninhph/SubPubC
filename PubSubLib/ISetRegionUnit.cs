namespace PubSubLib;

public interface ISetRegionUnit<T, TR>
    where T : IRegionUnit<TR>
{
    void SetRegionUnit(T region);
}