namespace PubSubLib.Mirror.Generator
{
    internal readonly struct FieldMapping
    {
        public readonly string FieldName;
        public readonly string PropertyName;
        public readonly string TypeName;
        public readonly bool IsRepeated;
        public readonly string ElementTypeName;
        public readonly bool IsReferenceType;
        public readonly int StructGroupIndex;
        public readonly int VectorGroupIndex;
        public FieldMapping(string fieldName, string propertyName, string typeName, bool isRepeated, string elementTypeName, bool isReferenceType, int structGroupIndex = -1, int vectorGroupIndex = -1)
        {
            FieldName = fieldName;
            PropertyName = propertyName;
            TypeName = typeName;
            IsRepeated = isRepeated;
            ElementTypeName = elementTypeName;
            IsReferenceType = isReferenceType;
            StructGroupIndex = structGroupIndex;
            VectorGroupIndex = vectorGroupIndex;
        }
    }
}
