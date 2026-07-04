namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UnitMirrorClientAttribute : Attribute
{
    public Type ProtoType { get; }
    public string? UnitType { get; set; }
    public UnitMirrorClientAttribute(Type protoType) => ProtoType = protoType;
}
