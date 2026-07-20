namespace PubSubLib.Mirror.Generator
{
    internal sealed class StructGroup
    {
        public readonly string StructName;
        public readonly string FieldName;
        public readonly string[] ProtoPropNames;
        public readonly string[] FieldPropNames;
        public readonly string[] ElementTypes;
        public readonly bool[] IsRefTypes;
        public readonly StructVector3Field[] Vector3Fields;
        public readonly StructPrimitiveArrayField[] PrimitiveArrayFields;
        public readonly bool IsClass;
        public StructGroup(string structName, string fieldName, string[] protoPropNames, string[] fieldPropNames, string[] elementTypes, bool[] isRefTypes, StructVector3Field[] vector3Fields, StructPrimitiveArrayField[] primitiveArrayFields, bool isClass = false)
        {
            StructName = structName;
            FieldName = fieldName;
            ProtoPropNames = protoPropNames;
            FieldPropNames = fieldPropNames;
            ElementTypes = elementTypes;
            IsRefTypes = isRefTypes;
            Vector3Fields = vector3Fields;
            PrimitiveArrayFields = primitiveArrayFields;
            IsClass = isClass;
        }
    }
}
