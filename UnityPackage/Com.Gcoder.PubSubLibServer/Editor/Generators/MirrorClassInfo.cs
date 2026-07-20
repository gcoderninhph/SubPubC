namespace PubSubLib.Mirror.Generator
{
    internal readonly struct MirrorClassInfo
    {
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string ProtoTypeFullName;
        public readonly string DataName;
        public readonly FieldMapping[] Fields;
        public readonly StructGroup[] StructGroups;
        public readonly VectorGroup[] VectorGroups;
        public MirrorClassInfo(string ns, string className, string protoTypeFullName, string dataName, FieldMapping[] fields, StructGroup[] structGroups, VectorGroup[] vectorGroups)
        {
            Namespace = ns;
            ClassName = className;
            ProtoTypeFullName = protoTypeFullName;
            DataName = dataName;
            Fields = fields;
            StructGroups = structGroups;
            VectorGroups = vectorGroups;
        }
    }
}