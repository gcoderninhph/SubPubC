namespace PubSubLib.Mirror.Generator
{
    internal readonly struct UnitMirrorClassInfo
    {
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string ProtoTypeFullName;
        public readonly string UnitType;
        public readonly string? TargetTypeFullName;
        public readonly FieldMapping[] Fields;
        public readonly StructGroup[] StructGroups;
        public readonly VectorGroup[] VectorGroups;
        public UnitMirrorClassInfo(string ns, string className, string protoTypeFullName, string unitType, string? targetTypeFullName, FieldMapping[] fields, StructGroup[] structGroups, VectorGroup[] vectorGroups)
        {
            Namespace = ns;
            ClassName = className;
            ProtoTypeFullName = protoTypeFullName;
            UnitType = unitType;
            TargetTypeFullName = targetTypeFullName;
            Fields = fields;
            StructGroups = structGroups;
            VectorGroups = vectorGroups;
        }
    }

}