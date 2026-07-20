namespace PubSubLib.Mirror.Generator
{
    internal sealed class StructPrimitiveArrayField
    {
        public readonly string FieldName;
        public readonly string ValueProtoName;
        public readonly string CountProtoName;
        public readonly string ElementType;
        public StructPrimitiveArrayField(string fieldName, string valueProtoName, string countProtoName, string elementType)
        {
            FieldName = fieldName;
            ValueProtoName = valueProtoName;
            CountProtoName = countProtoName;
            ElementType = elementType;
        }
    }
}