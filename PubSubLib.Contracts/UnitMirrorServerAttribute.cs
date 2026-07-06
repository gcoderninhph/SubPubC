namespace PubSubLib.Mirror;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UnitMirrorServerAttribute : Attribute
{
    public Type ProtoType { get; }
    public Type? Target { get; set; }
    public string? UnitType { get; set; }
    public UnitMirrorServerAttribute(Type protoType) => ProtoType = protoType;
}
