namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MirrorProtoAttribute : Attribute
{
    public Type ProtoType { get; }
    public MirrorProtoAttribute(Type protoType) => ProtoType = protoType;
}
