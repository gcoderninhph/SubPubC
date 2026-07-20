using System;
using PubSubLib.Mirror;

namespace PubSubLib.Client
{
    public interface IPlayerSpeaksClient : IDisposable
    {
        void AddData<T>() where T : class, IPlayerMirrorClient, new();
    
        T? GetData<T>() where T : class, IPlayerMirrorClient;
    
        void RemoveData<T>() where T : class, IPlayerMirrorClient;
    }

}

