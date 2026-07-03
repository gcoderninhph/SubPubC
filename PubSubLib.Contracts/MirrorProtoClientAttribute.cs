namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MirrorProtoClientAttribute : Attribute
{
    public Type ProtoType { get; }
    public string? DataName { get; set; }
    public MirrorProtoClientAttribute(Type protoType) => ProtoType = protoType;
}
