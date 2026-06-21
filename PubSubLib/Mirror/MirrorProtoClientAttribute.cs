namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MirrorProtoClientAttribute : Attribute
{
    public Type ProtoType { get; }
    public MirrorProtoClientAttribute(Type protoType) => ProtoType = protoType;
}
