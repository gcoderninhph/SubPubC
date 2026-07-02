namespace PubSubLib.Client;


public interface ISetRegionUnit<T, TR>
    where T : IRegionUnit<TR>
{
    void SetRegionUnit(T region);
}