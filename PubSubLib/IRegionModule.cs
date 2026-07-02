namespace PubSubLib;

public interface IRegionModule
{
    public static IRegionModule Create(RegionConfig config)
    {
        throw new NotImplementedException();
    }
    
    

    Task<IRegionUnit<T>> CreateUnitAsync<T>(long id, string type, Vector2 position, T target);
    void CreateUnit<T>(long id, string type, Vector2 position, T target, Action<IRegionUnit<T>> callback);
    Task<IRegionUnit<T>> CreateUnitAsync<T>(long id, string type, Vector2 position, Func<T> target);
    void CreateUnit<T>(long id, string type, Vector2 position, Func<T> target, Action<IRegionUnit<T>> callback);

    IRegionUnit<T> GetUnit<T>(long id) where T : IAlive;
    object GetUnit(string type, long id);
    IList<IRegionUnit<T>> GetUnits<T>() where T : IAlive;

    void DestroyUnit<T>(long id) where T : IAlive;
    void DestroyUnit(string type, long id);
}