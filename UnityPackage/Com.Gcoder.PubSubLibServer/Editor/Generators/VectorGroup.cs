namespace PubSubLib.Mirror.Generator
{
    internal sealed class VectorGroup
    {
        public readonly string FieldName;
        public readonly string PropertyName;
        public readonly string ProtoPropName;
        public readonly bool IsList;
        public VectorGroup(string fieldName, string propertyName, string protoPropName, bool isList)
        {
            FieldName = fieldName;
            PropertyName = propertyName;
            ProtoPropName = protoPropName;
            IsList = isList;
        }
    }
}