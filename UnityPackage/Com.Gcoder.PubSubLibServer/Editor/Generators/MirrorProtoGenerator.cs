using System.Collections.Generic;
using System.Linq;

namespace PubSubLib.Mirror.Generator
{
    public static class MirrorProtoGenerator
    {
        internal static List<string> SplitPascalCase(string name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;
            int start = 0;
            for (int i = 1; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]))
                {
                    words.Add(name.Substring(start, i - start));
                    start = i;
                }
            }

            words.Add(name.Substring(start));
            return words;
        }

        private static string ToFieldName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return "_";
            return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }

        internal static (StructGroup[] groups, int[] structGroupIndex) DetectStructGroups(List<FieldMapping> fields)
        {
            var candidates = new List<(int index, string structName, string fieldName, bool isClass)>();

            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (!f.IsRepeated) continue;

                var words = SplitPascalCase(f.PropertyName);
                int firstX = words.IndexOf("X");
                int lastX = words.LastIndexOf("X");
                if (firstX < 0 || lastX < 0 || firstX == lastX) continue;

                bool isClass = firstX > 0 && words[firstX - 1] == "Class";

                var structName = string.Concat(words.Skip(firstX + 1).Take(lastX - firstX - 1));
                var fieldName = string.Concat(words.Skip(lastX + 1));

                if (string.IsNullOrEmpty(structName) || string.IsNullOrEmpty(fieldName)) continue;

                candidates.Add((i, structName, fieldName, isClass));
            }

            var groups = new List<StructGroup>();
            var index = new int[fields.Count];
            for (int i = 0; i < fields.Count; i++) index[i] = -1;

            foreach (var grp in candidates.GroupBy(c => (c.structName, c.isClass)))
            {
                var members = grp.ToList();
                if (members.Count < 2) continue;

                var structName = grp.Key.structName;
                var isClass = grp.Key.isClass;
                var fieldName = ToFieldName(structName + "s");

                var protoPropNames = new string[members.Count];
                var fieldPropNames = new string[members.Count];
                var elementTypes = new string[members.Count];
                var isRefTypes = new bool[members.Count];

                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    protoPropNames[i] = fields[m.index].PropertyName;
                    fieldPropNames[i] = m.fieldName;
                    elementTypes[i] = fields[m.index].ElementTypeName;
                    isRefTypes[i] = fields[m.index].IsReferenceType;
                    index[m.index] = groups.Count;
                }

                var vec3Fields = new List<StructVector3Field>();
                var vecUsedIndices = new HashSet<int>();
                for (int vi = 0; vi < members.Count; vi++)
                {
                    if (vecUsedIndices.Contains(vi)) continue;
                    var vfn = fieldPropNames[vi];
                    if (!vfn.EndsWith("Vector3SValue") || vfn.Length <= 13) continue;
                    if (elementTypes[vi] != "float") continue;
                    var prefix = vfn.Substring(0, vfn.Length - 13);
                    for (int ci = 0; ci < members.Count; ci++)
                    {
                        if (ci == vi || vecUsedIndices.Contains(ci)) continue;
                        var cfn = fieldPropNames[ci];
                        if (!cfn.EndsWith("Vector3SCount") || cfn.Length <= 13) continue;
                        if (elementTypes[ci] != "int") continue;
                        if (cfn.Substring(0, cfn.Length - 13) == prefix)
                        {
                            vec3Fields.Add(new StructVector3Field(prefix, protoPropNames[vi], protoPropNames[ci]));
                            vecUsedIndices.Add(vi);
                            vecUsedIndices.Add(ci);
                            break;
                        }
                    }
                }

                for (int si = 0; si < members.Count; si++)
                {
                    if (vecUsedIndices.Contains(si)) continue;
                    var sfn = fieldPropNames[si];
                    if (!sfn.EndsWith("Vector3") || sfn.Length <= 7) continue;
                    if (sfn.EndsWith("Vector3SValue") || sfn.EndsWith("Vector3SCount")) continue;
                    if (elementTypes[si] != "float") continue;
                    var prefix = sfn.Substring(0, sfn.Length - 7);
                    vec3Fields.Add(new StructVector3Field(prefix, protoPropNames[si], "", isArray: false));
                    vecUsedIndices.Add(si);
                }

                var primArrayFields = new List<StructPrimitiveArrayField>();
                var primUsedIndices = new HashSet<int>(vecUsedIndices);
                for (int ai = 0; ai < members.Count; ai++)
                {
                    if (primUsedIndices.Contains(ai)) continue;
                    var afn = fieldPropNames[ai];
                    if (!afn.EndsWith("ArrayValue") || afn.Length <= 10) continue;
                    var prefix = afn.Substring(0, afn.Length - 10);
                    for (int ci = 0; ci < members.Count; ci++)
                    {
                        if (ci == ai || primUsedIndices.Contains(ci)) continue;
                        var cfn = fieldPropNames[ci];
                        if (!cfn.EndsWith("ArrayCount") || cfn.Length <= 10) continue;
                        if (elementTypes[ci] != "int") continue;
                        if (cfn.Substring(0, cfn.Length - 10) == prefix)
                        {
                            primArrayFields.Add(new StructPrimitiveArrayField(prefix, protoPropNames[ai],
                                protoPropNames[ci], elementTypes[ai]));
                            primUsedIndices.Add(ai);
                            primUsedIndices.Add(ci);
                            break;
                        }
                    }
                }

                var filteredProtoPropNames = new List<string>();
                var filteredFieldPropNames = new List<string>();
                var filteredElementTypes = new List<string>();
                var filteredIsRefTypes = new List<bool>();
                for (int i = 0; i < members.Count; i++)
                {
                    if (primUsedIndices.Contains(i)) continue;
                    filteredProtoPropNames.Add(protoPropNames[i]);
                    filteredFieldPropNames.Add(fieldPropNames[i]);
                    filteredElementTypes.Add(elementTypes[i]);
                    filteredIsRefTypes.Add(isRefTypes[i]);
                }

                groups.Add(new StructGroup(structName, fieldName,
                    filteredProtoPropNames.ToArray(), filteredFieldPropNames.ToArray(),
                    filteredElementTypes.ToArray(), filteredIsRefTypes.ToArray(),
                    vec3Fields.ToArray(), primArrayFields.ToArray(), isClass));
            }

            return (groups.ToArray(), index);
        }

        internal static (VectorGroup[] groups, int[] vectorGroupIndex) DetectVectorGroups(List<FieldMapping> fields)
        {
            var groups = new List<VectorGroup>();
            var index = new int[fields.Count];
            for (int i = 0; i < fields.Count; i++) index[i] = -1;

            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (!f.IsRepeated) continue;
                if (f.ElementTypeName != "float") continue;
                if (f.StructGroupIndex >= 0) continue;

                if (f.PropertyName.EndsWith("Vector3S") && f.PropertyName.Length > 8)
                {
                    var propName = f.PropertyName.Substring(0, f.PropertyName.Length - 8);
                    var fieldName = ToFieldName(propName);
                    groups.Add(new VectorGroup(fieldName, propName, f.PropertyName, true));
                    index[i] = groups.Count - 1;
                }
                else if (f.PropertyName.EndsWith("Vector3") && f.PropertyName.Length > 7)
                {
                    var propName = f.PropertyName.Substring(0, f.PropertyName.Length - 7);
                    var fieldName = ToFieldName(propName);
                    groups.Add(new VectorGroup(fieldName, propName, f.PropertyName, false));
                    index[i] = groups.Count - 1;
                }
            }

            return (groups.ToArray(), index);
        }
    }
}