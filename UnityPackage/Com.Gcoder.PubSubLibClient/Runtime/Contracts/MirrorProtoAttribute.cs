using System;

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


