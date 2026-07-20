using Google.Protobuf;

namespace PubSubLib
{
    public interface IUnitSt<T, TR> where T : class, IAlive where TR : class, IMessage<TR>
    {
        long Id { get; }
        string Type { get; }
        Vector2 Position { get; set; }
        bool IsAlive { get; }
        T Target { get; }
        int Version { get; }
        TR Data { get; set; }
        void PublishEvent<TE>(string eventName, TE data, bool reliable = true) where TE : IMessage;
        void Destroy();
    }
}