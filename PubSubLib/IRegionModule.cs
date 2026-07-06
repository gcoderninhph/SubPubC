namespace PubSubLib;

public interface IRegionModule : IDisposable
{
    public static IRegionModule Create(RegionConfig config)
    {
        return new RegionModule(config);
    }

    Task<T> CreateUnitAsync<T, TR>(long id, Vector2 position, TR target)
        where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

    void CreateUnit<T, TR>(long id, Vector2 position, TR target, Action<T> callback)
        where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

    Task<T> CreateUnitAsync<T, TR>(long id, Vector2 position, Func<TR> target)
        where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

    void CreateUnit<T, TR>(long id, Vector2 position, Func<TR> target, Action<T> callback)
        where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

    T GetUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;
    IList<T> GetUnits<T, TR>() where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;

    void DestroyUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;
}