using Google.Protobuf;
using MyConnection;

namespace PubSubLib.Client;

public interface IRegionUnit
{
    long Id { get; }
    string UnitType { get; }
    ISubscribe OnMessage<TProto>(string subject, Action<TProto> callback) where TProto : class, IMessage<TProto>, new();
}
