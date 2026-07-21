using System;
using Google.Protobuf;
using MyConnection;

namespace PubSubLib.Client
{
    public interface IRegionUnit
    {
        long Id { get; }
        string UnitType { get; }
        UnityEngine.Object GetTarget();
        ISubscribe OnMessage<TProto>(string subject, Action<TProto> callback)
            where TProto : class, IMessage<TProto>, new();
    }

    public interface IRegionUnit<TR> : IRegionUnit where TR : UnityEngine.Object
    {
        new TR GetTarget();
        UnityEngine.Object IRegionUnit.GetTarget() => GetTarget();
    }
}