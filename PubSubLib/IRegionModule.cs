namespace PubSubLib;

public interface IRegionModule
{
    public static IRegionModule Create(RegionConfig config)
    {
        return RegionModule.Create(config);
    }

    Task<T> CreateUnitAsync<T, TR>(long id, string type, Vector2 position, TR target)
        where T : class, IRegionUnit<TR>, new();

    void CreateUnit<T, TR>(long id, string type, Vector2 position, TR target, Action<T> callback)
        where T : class, IRegionUnit<TR>, new();

    Task<T> CreateUnitAsync<T, TR>(long id, string type, Vector2 position, Func<TR> target)
        where T : class, IRegionUnit<TR>, new();

    void CreateUnit<T, TR>(long id, string type, Vector2 position, Func<TR> target, Action<T> callback)
        where T : class, IRegionUnit<TR>, new();

    T GetUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new();
    object GetUnit(string type, long id);
    IList<T> GetUnits<T, TR>() where T : class, IRegionUnit<TR>, new();

    void DestroyUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new();
    void DestroyUnit(string type, long id);
}