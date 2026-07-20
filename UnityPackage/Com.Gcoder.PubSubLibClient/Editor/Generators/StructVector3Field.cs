namespace PubSubLib.Mirror.Generator
{
    internal sealed class StructVector3Field
    {
        public readonly string FieldName;
        public readonly string ValueProtoName;
        public readonly string CountProtoName;
        public readonly bool IsArray;
        public StructVector3Field(string fieldName, string valueProtoName, string countProtoName, bool isArray = true)
        {
            FieldName = fieldName;
            ValueProtoName = valueProtoName;
            CountProtoName = countProtoName;
            IsArray = isArray;
        }
    }
}
