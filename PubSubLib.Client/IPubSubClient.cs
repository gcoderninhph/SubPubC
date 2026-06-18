#if UNITY_ENGINE
using Vector2 = UnityEngine.Vector2;
#endif
namespace PubSubLib.Client;

public interface IPubSubClient
{
    void Tick();
    void MoveWatcher(Vector2 postion, float radius);
    IPubSubClient AddProvider(IProvider provider);
}