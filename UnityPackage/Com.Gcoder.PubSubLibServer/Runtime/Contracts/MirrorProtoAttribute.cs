using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib.Mirror
{

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MirrorProtoAttribute : Attribute
{
    public Type ProtoType { get; }
    public string? DataName { get; set; }
    public MirrorProtoAttribute(Type protoType) => ProtoType = protoType;
}
}
