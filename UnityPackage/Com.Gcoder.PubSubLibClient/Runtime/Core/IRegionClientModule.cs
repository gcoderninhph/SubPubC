using System;
using System.Collections.Generic;
using MyConnection;

namespace PubSubLib.Client
{
    public interface IRegionClientModule : IClientModule, IDisposable
    {
        static IRegionClientModule Create(Config config)
        {
            return new RegionClientModule(config);
        }
    
        T? GetUnit<T, TR>(long id) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;
        bool TryGetUnit<T, TR>(long id, out T unit) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;
        IList<T> GetUnits<T, TR>() where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;
    
        void OnCreateUnit<T, TR>(Func<T, TR> unit) where T : class, IRegionUnit<TR>, new() where TR : class, IAlive;
        void Destroy<T, TR>(T unit) where T : class, IRegionUnit<TR> where TR : class, IAlive;
        void Tick();
        void MoveWatcher(Vector2 position, float range);
    }

}

