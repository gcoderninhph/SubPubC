using System;
using Google.Protobuf;
using MyConnection;

namespace PubSubLib.Client
{
    public interface IRegionUnit<TR> where TR : class, IAlive
    {
        long Id { get; }
        string UnitType { get; }
        TR GetTarget();
        ISubscribe OnMessage<TProto>(string subject, Action<TProto> callback) where TProto : class, IMessage<TProto>, new();
    }
}


