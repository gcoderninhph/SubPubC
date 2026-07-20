using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PubSubLib.Mirror.Generator
{
    /// <summary>
    /// Bản "runtime" của UnitMirrorGenerator: thay vì chạy trong Roslyn compiler pipeline
    /// (ISymbol / GeneratorAttributeSyntaxContext), hàm này dùng System.Reflection để đọc
    /// metadata từ một Type đã compile sẵn, rồi ghi thẳng ra file .cs trên đĩa.
    ///
    /// Giả định (cần chỉnh nếu khác với project thật):
    /// - typeClass là class đã gắn [UnitMirrorServerAttribute(protoType, UnitType = ..., Target = ...)]
    /// - UnitMirrorServerAttribute có property: ProtoType (Type, ctor arg), UnitType (string?), Target (Type?)
    /// - FieldMapping / StructGroup / VectorGroup / UnitMirrorClassInfo / MirrorProtoGenerator
    ///   đã tồn tại sẵn trong project (định nghĩa ở nơi khác, không đổi ở đây)
    /// </summary>
    public static class UnitMirrorFileGenerator
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> CSharpAliases = new()
        {
            ["System.Boolean"] = "bool",
            ["System.Byte"] = "byte",
            ["System.SByte"] = "sbyte",
            ["System.Char"] = "char",
            ["System.Decimal"] = "decimal",
            ["System.Double"] = "double",
            ["System.Single"] = "float",
            ["System.Int32"] = "int",
            ["System.UInt32"] = "uint",
            ["System.Int64"] = "long",
            ["System.UInt64"] = "ulong",
            ["System.Int16"] = "short",
            ["System.UInt16"] = "ushort",
            ["System.String"] = "string",
            ["System.Object"] = "object",
        };

        public static void GenerateFile(Type typeClass, string pathOutPut)
        {
            var info = BuildClassInfo(typeClass)
                       ?? throw new InvalidOperationException(
                           $"'{typeClass.FullName}' không hợp lệ để sinh UnitMirror " +
                           $"(thiếu [UnitMirrorServerAttribute] hoặc thiếu ProtoType).");

            var source = BuildSource(info);

            var dir = Path.GetDirectoryName(pathOutPut);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(pathOutPut, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        // ================= Bước 1: đọc metadata bằng Reflection (thay TransformClass) =================

        private static UnitMirrorClassInfo? BuildClassInfo(Type typeClass)
        {
            var attr = typeClass.GetCustomAttribute<UnitMirrorServerAttribute>();
            if (attr is null) return null;

            var protoType = attr.ProtoType;
            if (protoType is null) return null;

            var unitType = attr.UnitType ?? protoType.Name;
            var targetTypeFullName = attr.Target is not null
                ? "global::" + GetFriendlyTypeName(attr.Target)
                : "global::PubSubLib.IAlive";

            var ns = typeClass.Namespace ?? "";

            var fields = new List<FieldMapping>();
            var reserved = new HashSet<string> { "PlayerId", "IsOnLine", "DataName", "UnitType", "Id" };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var prop in protoType.GetProperties(flags))
            {
                if (prop.DeclaringType != protoType) continue;
                if (prop.GetIndexParameters().Length > 0) continue;

                var getter = prop.GetGetMethod(nonPublic: true);
                if (getter is null || getter.IsStatic) continue;

                // heuristic thay cho ExplicitInterfaceImplementations của Roslyn
                if (prop.Name.Contains('.')) continue;

                var propName = prop.Name;
                if (reserved.Contains(propName)) continue;
                if (propName.EndsWith("Case") && prop.PropertyType.IsEnum) continue;

                var fieldName = ToFieldName(propName);
                var typeName = GetFriendlyTypeName(prop.PropertyType);

                var isRepeated = false;
                var elementTypeName = typeName;
                if (prop.PropertyType.IsGenericType
                    && prop.PropertyType.GetGenericTypeDefinition().Name == "RepeatedField`1"
                    && prop.PropertyType.Namespace == "Google.Protobuf.Collections")
                {
                    isRepeated = true;
                    elementTypeName = GetFriendlyTypeName(prop.PropertyType.GetGenericArguments()[0]);
                }

                fields.Add(new FieldMapping(fieldName, propName, typeName, isRepeated, elementTypeName,
                    !prop.PropertyType.IsValueType));
            }

            var fullProtoName = "global::" + GetFriendlyTypeName(protoType);

            var (structGroups, structGroupIndex) = MirrorProtoGenerator.DetectStructGroups(fields);
            for (int i = 0; i < fields.Count; i++)
            {
                if (structGroupIndex[i] >= 0)
                {
                    var f = fields[i];
                    fields[i] = new FieldMapping(f.FieldName, f.PropertyName, f.TypeName, f.IsRepeated,
                        f.ElementTypeName, f.IsReferenceType, structGroupIndex[i]);
                }
            }

            var (vectorGroups, vectorGroupIndex) = MirrorProtoGenerator.DetectVectorGroups(fields);
            for (int i = 0; i < fields.Count; i++)
            {
                if (vectorGroupIndex[i] >= 0)
                {
                    var f = fields[i];
                    fields[i] = new FieldMapping(f.FieldName, f.PropertyName, f.TypeName, f.IsRepeated,
                        f.ElementTypeName, f.IsReferenceType, f.StructGroupIndex, vectorGroupIndex[i]);
                }
            }

            var serverCollisions = new HashSet<string> { "Position" };
            var validVectorGroups = new List<VectorGroup>();
            var vgRemap = new int[vectorGroups.Length];
            for (int i = 0; i < vectorGroups.Length; i++)
            {
                if (serverCollisions.Contains(vectorGroups[i].PropertyName))
                {
                    vgRemap[i] = -1;
                    for (int j = 0; j < fields.Count; j++)
                        if (fields[j].VectorGroupIndex == i)
                            fields[j] = new FieldMapping(fields[j].FieldName, fields[j].PropertyName,
                                fields[j].TypeName, fields[j].IsRepeated, fields[j].ElementTypeName,
                                fields[j].IsReferenceType, fields[j].StructGroupIndex, -1);
                }
                else
                {
                    vgRemap[i] = validVectorGroups.Count;
                    validVectorGroups.Add(vectorGroups[i]);
                }
            }

            for (int j = 0; j < fields.Count; j++)
            {
                if (fields[j].VectorGroupIndex >= 0)
                    fields[j] = new FieldMapping(fields[j].FieldName, fields[j].PropertyName, fields[j].TypeName,
                        fields[j].IsRepeated, fields[j].ElementTypeName, fields[j].IsReferenceType,
                        fields[j].StructGroupIndex, vgRemap[fields[j].VectorGroupIndex]);
            }

            vectorGroups = validVectorGroups.ToArray();

            return new UnitMirrorClassInfo(ns, typeClass.Name, fullProtoName, unitType, targetTypeFullName,
                fields.ToArray(), structGroups, vectorGroups);
        }

        private static string ToFieldName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return "_";
            return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }

        /// <summary>
        /// Tương đương SymbolDisplayFormat.FullyQualifiedFormat của Roslyn nhưng dựng từ System.Type.
        /// </summary>
        private static string GetFriendlyTypeName(Type t)
        {
            if (t.IsGenericType)
            {
                var name = t.Name.Substring(0, t.Name.IndexOf('`'));
                var args = string.Join(", ", t.GetGenericArguments().Select(GetFriendlyTypeName));
                var ns = string.IsNullOrEmpty(t.Namespace) ? "" : t.Namespace + ".";
                return $"{ns}{name}<{args}>";
            }

            if (t.IsArray)
                return GetFriendlyTypeName(t.GetElementType()!) + "[]";

            var fullName = t.FullName ?? t.Name;
            return CSharpAliases.TryGetValue(fullName, out var alias) ? alias : fullName.Replace('+', '.');
        }

        // ================= Bước 2: sinh source text (giữ nguyên logic GenerateCode gốc) =================

        private static string BuildSource(UnitMirrorClassInfo info)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Google.Protobuf;");
            sb.AppendLine("using PubSubLib;");
            sb.AppendLine("using PubSubLib.Contracts;");
            sb.AppendLine("using PubSubLib.Messages;");
            sb.AppendLine("using PubSubLib.Mirror;");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS8618");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
            }

            sb.AppendLine(
                $"partial class {info.ClassName} : global::PubSubLib.IRegionUnit<{info.TargetTypeFullName}>, global::PubSubLib.IRegionUnitInternal");
            sb.AppendLine("{");

            sb.AppendLine($"    public static string _unitType = \"{info.UnitType}\";");
            sb.AppendLine($"    public string UnitType => _unitType;");
            sb.AppendLine();
            sb.AppendLine("    private global::PubSubLib.IUnit _unit;");
            sb.AppendLine();
            sb.AppendLine(
                "    void global::PubSubLib.IRegionUnitInternal.SetUnit(global::PubSubLib.IUnit unit) => _unit = unit;");
            sb.AppendLine("    string global::PubSubLib.IRegionUnitInternal.GetUnitType() => _unitType;");
            sb.AppendLine("    global::PubSubLib.IUnit global::PubSubLib.IRegionUnitInternal.GetUnit() => _unit;");
            sb.AppendLine();
            sb.AppendLine("    public long Id => _unit.Id;");
            sb.AppendLine();
            sb.AppendLine("    public global::PubSubLib.Vector2 Position");
            sb.AppendLine("    {");
            sb.AppendLine("        get => _unit.Position;");
            sb.AppendLine("        set => _unit.Position = value;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public {info.TargetTypeFullName} Get() => ({info.TargetTypeFullName})_unit.Target!;");
            sb.AppendLine();
            sb.AppendLine("    public void SetPosition(float x, float y)");
            sb.AppendLine("    {");
            sb.AppendLine("        _unit.Position = new global::PubSubLib.Vector2 { x = x, y = y };");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public void SetPosition(global::PubSubLib.Vector2 position)");
            sb.AppendLine("    {");
            sb.AppendLine("        _unit.Position = position;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public void Destroy()");
            sb.AppendLine("    {");
            sb.AppendLine("        _unit.Destroy();");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine($"    private {info.ProtoTypeFullName}? _mirrorProto;");
            sb.AppendLine($"    public {info.ProtoTypeFullName} GetMirrorProto()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_mirrorProto is null)");
            sb.AppendLine($"            _mirrorProto = new {info.ProtoTypeFullName}();");
            sb.AppendLine("        return _mirrorProto;");
            sb.AppendLine("    }");
            sb.AppendLine();

            foreach (var sg in info.StructGroups)
            {
                sb.AppendLine();
                if (sg.IsClass)
                {
                    sb.AppendLine(
                        $"    public class {sg.StructName} : global::PubSubLib.Mirror.IMirrorListItemDirtyProxy");
                    sb.AppendLine("    {");
                    sb.AppendLine("        private System.Action? __markDirty;");
                    sb.AppendLine(
                        "        void global::PubSubLib.Mirror.IMirrorListItemDirtyProxy.SetDirtyMarker(System.Action? md)");
                    sb.AppendLine("            => __markDirty = md;");
                    sb.AppendLine();
                    for (int i = 0; i < sg.FieldPropNames.Length; i++)
                    {
                        var pf = sg.FieldPropNames[i];
                        var bf = "_" + char.ToLowerInvariant(pf[0]) + pf.Substring(1);
                        sb.AppendLine($"        private {sg.ElementTypes[i]} {bf};");
                        sb.AppendLine($"        public {sg.ElementTypes[i]} {pf}");
                        sb.AppendLine("        {");
                        sb.AppendLine($"            get => {bf};");
                        sb.AppendLine(
                            $"            set {{ if (!System.Collections.Generic.EqualityComparer<{sg.ElementTypes[i]}>.Default.Equals({bf}, value)) {{ {bf} = value; __markDirty?.Invoke(); }} }}");
                        sb.AppendLine("        }");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        if (vf.IsArray)
                        {
                            var bf = "_" + char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                            sb.AppendLine(
                                $"        private global::PubSubLib.Mirror.DirtyList<global::PubSubLib.Vector3> {bf};");
                            sb.AppendLine(
                                $"        public global::PubSubLib.Mirror.DirtyList<global::PubSubLib.Vector3> {vf.FieldName} => {bf};");
                        }
                        else
                        {
                            sb.AppendLine($"        public global::PubSubLib.Vector3 {vf.FieldName} {{ get; }}");
                        }
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        var bf = "_" + char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                        sb.AppendLine($"        private global::PubSubLib.Mirror.DirtyList<{pa.ElementType}> {bf};");
                        sb.AppendLine(
                            $"        public global::PubSubLib.Mirror.DirtyList<{pa.ElementType}> {pa.FieldName} => {bf};");
                    }

                    sb.AppendLine();
                    var ctorArgs = new List<string>();
                    for (int i = 0; i < sg.FieldPropNames.Length; i++)
                    {
                        ctorArgs.Add(
                            $"{sg.ElementTypes[i]} {char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        var argName = char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                        var v3Type = vf.IsArray
                            ? "System.Collections.Generic.IReadOnlyList<global::PubSubLib.Vector3>"
                            : "global::PubSubLib.Vector3";
                        ctorArgs.Add($"{v3Type} {argName}");
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        var argName = char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                        ctorArgs.Add($"System.Collections.Generic.IReadOnlyList<{pa.ElementType}> {argName}");
                    }

                    sb.AppendLine($"        public {sg.StructName}({string.Join(", ", ctorArgs)})");
                    sb.AppendLine("        {");
                    for (int i = 0; i < sg.FieldPropNames.Length; i++)
                    {
                        var pf = sg.FieldPropNames[i];
                        var bf = "_" + char.ToLowerInvariant(pf[0]) + pf.Substring(1);
                        var argName = $"{char.ToLowerInvariant(pf[0])}{pf.Substring(1)}";
                        sb.AppendLine($"            {bf} = {argName};");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        var argName = char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                        if (vf.IsArray)
                        {
                            var bf = "_" + char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                            sb.AppendLine(
                                $"            {bf} = new global::PubSubLib.Mirror.DirtyList<global::PubSubLib.Vector3>({argName}, () => __markDirty?.Invoke());");
                        }
                        else
                        {
                            sb.AppendLine($"            {vf.FieldName} = {argName};");
                        }
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        var bf = "_" + char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                        var argName = char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                        sb.AppendLine(
                            $"            {bf} = new global::PubSubLib.Mirror.DirtyList<{pa.ElementType}>({argName}, () => __markDirty?.Invoke());");
                    }

                    sb.AppendLine("        }");
                    sb.AppendLine("    }");
                }
                else
                {
                    var hasDirtyFields = sg.Vector3Fields.Any(vf => vf.IsArray) || sg.PrimitiveArrayFields.Length > 0;
                    var structDecl = hasDirtyFields
                        ? $"    public struct {sg.StructName} : global::PubSubLib.Mirror.IMirrorListItemDirtyProxy"
                        : $"    public struct {sg.StructName}";
                    sb.AppendLine(structDecl);
                    sb.AppendLine("    {");
                    for (int i = 0; i < sg.FieldPropNames.Length; i++)
                    {
                        sb.AppendLine($"        public {sg.ElementTypes[i]} {sg.FieldPropNames[i]} {{ get; }}");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        if (vf.IsArray)
                        {
                            sb.AppendLine(
                                $"        public global::PubSubLib.Mirror.DirtyList<global::PubSubLib.Vector3> {vf.FieldName};");
                        }
                        else
                        {
                            sb.AppendLine($"        public global::PubSubLib.Vector3 {vf.FieldName} {{ get; }}");
                        }
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        sb.AppendLine(
                            $"        public global::PubSubLib.Mirror.DirtyList<{pa.ElementType}> {pa.FieldName};");
                    }

                    if (hasDirtyFields)
                    {
                        sb.AppendLine(
                            "        void global::PubSubLib.Mirror.IMirrorListItemDirtyProxy.SetDirtyMarker(System.Action? md)");
                        sb.AppendLine("        {");
                        foreach (var vf in sg.Vector3Fields)
                        {
                            if (vf.IsArray)
                                sb.AppendLine($"            {vf.FieldName}.SetDirtyCallback(md);");
                        }

                        foreach (var pa in sg.PrimitiveArrayFields)
                        {
                            sb.AppendLine($"            {pa.FieldName}.SetDirtyCallback(md);");
                        }

                        sb.AppendLine("        }");
                    }

                    sb.AppendLine();
                    var ctorArgs = new List<string>();
                    for (int i = 0; i < sg.FieldPropNames.Length; i++)
                    {
                        ctorArgs.Add(
                            $"{sg.ElementTypes[i]} {char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        var argName = char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                        var v3Type = vf.IsArray
                            ? "System.Collections.Generic.IReadOnlyList<global::PubSubLib.Vector3>"
                            : "global::PubSubLib.Vector3";
                        ctorArgs.Add($"{v3Type} {argName}");
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        var argName = char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                        ctorArgs.Add($"System.Collections.Generic.IReadOnlyList<{pa.ElementType}> {argName}");
                    }

                    sb.AppendLine($"        public {sg.StructName}({string.Join(", ", ctorArgs)})");
                    sb.AppendLine("        {");
                    for (int i = 0; i < sg.FieldPropNames.Length; i++)
                    {
                        var argName =
                            $"{char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}";
                        sb.AppendLine($"            {sg.FieldPropNames[i]} = {argName};");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        var argName = char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                        if (vf.IsArray)
                        {
                            sb.AppendLine(
                                $"            {vf.FieldName} = new global::PubSubLib.Mirror.DirtyList<global::PubSubLib.Vector3>({argName}, null);");
                        }
                        else
                        {
                            sb.AppendLine($"            {vf.FieldName} = {argName};");
                        }
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        var argName = char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                        sb.AppendLine(
                            $"            {pa.FieldName} = new global::PubSubLib.Mirror.DirtyList<{pa.ElementType}>({argName}, null);");
                    }

                    sb.AppendLine("        }");
                    sb.AppendLine("    }");
                }
            }

            foreach (var f in info.Fields)
            {
                if (f.StructGroupIndex >= 0) continue;
                if (f.VectorGroupIndex >= 0) continue;
                if (f.IsRepeated)
                {
                    var listType = $"global::PubSubLib.Mirror.MirrorRepeatedList<{f.ElementTypeName}>";
                    sb.AppendLine($"    private readonly {listType} {f.FieldName} = new();");
                    sb.AppendLine();
                    sb.AppendLine($"    public {listType} {f.PropertyName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        get => {f.FieldName};");
                    sb.AppendLine("    }");
                }
                else
                {
                    sb.AppendLine($"    private {f.TypeName} {f.FieldName};");
                    sb.AppendLine();
                    sb.AppendLine($"    public {f.TypeName} {f.PropertyName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        get => {f.FieldName};");
                    sb.AppendLine($"        set => {f.FieldName} = value;");
                    sb.AppendLine("    }");
                }

                sb.AppendLine();
            }

            foreach (var sg in info.StructGroups)
            {
                var listType = $"global::PubSubLib.Mirror.MirrorRepeatedList<{sg.StructName}>";
                sb.AppendLine($"    private readonly {listType} {sg.FieldName} = new();");
                sb.AppendLine();
                sb.AppendLine($"    public {listType} {sg.StructName}s");
                sb.AppendLine("    {");
                sb.AppendLine($"        get => {sg.FieldName};");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            foreach (var vg in info.VectorGroups)
            {
                if (vg.IsList)
                {
                    var listType = "global::PubSubLib.Mirror.MirrorRepeatedList<global::PubSubLib.Vector3>";
                    sb.AppendLine($"    private readonly {listType} {vg.FieldName} = new();");
                    sb.AppendLine();
                    sb.AppendLine($"    public {listType} {vg.PropertyName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        get => {vg.FieldName};");
                    sb.AppendLine("    }");
                }
                else
                {
                    sb.AppendLine($"    private global::PubSubLib.Vector3 {vg.FieldName};");
                    sb.AppendLine();
                    sb.AppendLine($"    public global::PubSubLib.Vector3 {vg.PropertyName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        get => {vg.FieldName};");
                    sb.AppendLine($"        set => {vg.FieldName} = value;");
                    sb.AppendLine("    }");
                }

                sb.AppendLine();
            }

            var hasAnyField = info.Fields.Any(f => f.StructGroupIndex < 0 && f.VectorGroupIndex < 0);
            var hasAnyStructGroup = info.StructGroups.Length > 0;
            var hasAnyVectorGroup = info.VectorGroups.Length > 0;
            if (hasAnyField || hasAnyStructGroup || hasAnyVectorGroup)
            {
                sb.AppendLine("    public void Commit(string commit)");
                sb.AppendLine("    {");
                sb.AppendLine("        var proto = GetMirrorProto();");
                foreach (var sg in info.StructGroups)
                {
                    sb.AppendLine($"        {sg.StructName}[]? ___arr_{sg.FieldName} = {sg.FieldName}.TrySnapshot();");
                }

                foreach (var vg in info.VectorGroups)
                {
                    if (vg.IsList)
                    {
                        sb.AppendLine(
                            $"        global::PubSubLib.Vector3[]? ___arr_{vg.FieldName} = {vg.FieldName}.TrySnapshot();");
                    }
                }

                foreach (var f in info.Fields)
                {
                    if (f.StructGroupIndex >= 0) continue;
                    if (f.VectorGroupIndex >= 0) continue;
                    if (f.IsRepeated)
                        sb.AppendLine(
                            $"        {f.ElementTypeName}[]? ___arr_{f.FieldName} = {f.FieldName}.TrySnapshot();");
                }

                sb.AppendLine("        MirrorProtoBus.Enqueue(proto,");
                sb.AppendLine("            __bytes =>");
                sb.AppendLine("            {");
                sb.AppendLine("                _unit.Data = __bytes;");
                sb.AppendLine(
                    "                var __commit = new RegionCommit { Commit = commit, MirrorData = Google.Protobuf.ByteString.CopyFrom(__bytes) };");
                sb.AppendLine("                _unit.PublishEvent(\"commit\", __commit.ToByteArray(), true);");
                sb.AppendLine("            },");
                sb.AppendLine("            __p =>");
                sb.AppendLine("            {");
                foreach (var sg in info.StructGroups)
                {
                    sb.AppendLine($"                if (___arr_{sg.FieldName} is not null)");
                    sb.AppendLine("                {");
                    for (int i = 0; i < sg.ProtoPropNames.Length; i++)
                    {
                        sb.AppendLine($"                    __p.{sg.ProtoPropNames[i]}.Clear();");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        sb.AppendLine($"                    __p.{vf.ValueProtoName}.Clear();");
                        if (vf.IsArray)
                            sb.AppendLine($"                    __p.{vf.CountProtoName}.Clear();");
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        sb.AppendLine($"                    __p.{pa.ValueProtoName}.Clear();");
                        sb.AppendLine($"                    __p.{pa.CountProtoName}.Clear();");
                    }

                    sb.AppendLine($"                    foreach (var __item in ___arr_{sg.FieldName})");
                    sb.AppendLine("                    {");
                    for (int i = 0; i < sg.ProtoPropNames.Length; i++)
                    {
                        sb.AppendLine(
                            $"                        __p.{sg.ProtoPropNames[i]}.Add(__item.{sg.FieldPropNames[i]});");
                    }

                    foreach (var vf in sg.Vector3Fields)
                    {
                        if (vf.IsArray)
                        {
                            sb.AppendLine(
                                $"                        var ___v3s_{vf.FieldName} = __item.{vf.FieldName};");
                            sb.AppendLine(
                                $"                        __p.{vf.CountProtoName}.Add(___v3s_{vf.FieldName}?.Count ?? 0);");
                            sb.AppendLine($"                        if (___v3s_{vf.FieldName} is not null)");
                            sb.AppendLine($"                            foreach (var __v in ___v3s_{vf.FieldName})");
                            sb.AppendLine("                            {");
                            sb.AppendLine($"                                __p.{vf.ValueProtoName}.Add(__v.x);");
                            sb.AppendLine($"                                __p.{vf.ValueProtoName}.Add(__v.y);");
                            sb.AppendLine($"                                __p.{vf.ValueProtoName}.Add(__v.z);");
                            sb.AppendLine("                            }");
                        }
                        else
                        {
                            sb.AppendLine(
                                $"                        __p.{vf.ValueProtoName}.Add(__item.{vf.FieldName}.x);");
                            sb.AppendLine(
                                $"                        __p.{vf.ValueProtoName}.Add(__item.{vf.FieldName}.y);");
                            sb.AppendLine(
                                $"                        __p.{vf.ValueProtoName}.Add(__item.{vf.FieldName}.z);");
                        }
                    }

                    foreach (var pa in sg.PrimitiveArrayFields)
                    {
                        sb.AppendLine($"                        var ___arr_{pa.FieldName} = __item.{pa.FieldName};");
                        sb.AppendLine(
                            $"                        __p.{pa.CountProtoName}.Add(___arr_{pa.FieldName}?.Count ?? 0);");
                        sb.AppendLine($"                        if (___arr_{pa.FieldName} is not null)");
                        sb.AppendLine($"                            foreach (var __v in ___arr_{pa.FieldName})");
                        sb.AppendLine($"                                __p.{pa.ValueProtoName}.Add(__v);");
                    }

                    sb.AppendLine("                    }");
                    sb.AppendLine("                }");
                }

                foreach (var vg in info.VectorGroups)
                {
                    if (vg.IsList)
                    {
                        sb.AppendLine($"                if (___arr_{vg.FieldName} is not null)");
                        sb.AppendLine("                {");
                        sb.AppendLine($"                    __p.{vg.ProtoPropName}.Clear();");
                        sb.AppendLine($"                    foreach (var __item in ___arr_{vg.FieldName})");
                        sb.AppendLine("                    {");
                        sb.AppendLine($"                        __p.{vg.ProtoPropName}.Add(__item.x);");
                        sb.AppendLine($"                        __p.{vg.ProtoPropName}.Add(__item.y);");
                        sb.AppendLine($"                        __p.{vg.ProtoPropName}.Add(__item.z);");
                        sb.AppendLine("                    }");
                        sb.AppendLine("                }");
                    }
                    else
                    {
                        sb.AppendLine($"                __p.{vg.ProtoPropName}.Clear();");
                        sb.AppendLine($"                __p.{vg.ProtoPropName}.Add({vg.FieldName}.x);");
                        sb.AppendLine($"                __p.{vg.ProtoPropName}.Add({vg.FieldName}.y);");
                        sb.AppendLine($"                __p.{vg.ProtoPropName}.Add({vg.FieldName}.z);");
                    }
                }

                foreach (var f in info.Fields)
                {
                    if (f.StructGroupIndex >= 0) continue;
                    if (f.VectorGroupIndex >= 0) continue;
                    if (f.IsRepeated)
                    {
                        sb.AppendLine($"                if (___arr_{f.FieldName} is not null)");
                        sb.AppendLine("                {");
                        sb.AppendLine($"                    __p.{f.PropertyName}.Clear();");
                        sb.AppendLine($"                    __p.{f.PropertyName}.AddRange(___arr_{f.FieldName});");
                        sb.AppendLine("                }");
                    }
                    else if (f.IsReferenceType)
                    {
                        sb.AppendLine(
                            $"                if ({f.FieldName} is not null) __p.{f.PropertyName} = {f.FieldName};");
                    }
                    else
                    {
                        sb.AppendLine($"                __p.{f.PropertyName} = {f.FieldName};");
                    }
                }

                sb.AppendLine("            });");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("    public void SendMessage<TProto>(string subject, TProto message, bool reliable)");
            sb.AppendLine("        where TProto : class, global::Google.Protobuf.IMessage<TProto>, new()");
            sb.AppendLine("    {");
            sb.AppendLine("        MirrorProtoBus.EnqueueMessage(subject, message,");
            sb.AppendLine("            (__subject, __bytes) =>");
            sb.AppendLine("            {");
            sb.AppendLine(
                "                var __rmsg = new RegionMessage { Subject = __subject, Data = Google.Protobuf.ByteString.CopyFrom(__bytes) };");
            sb.AppendLine("                _unit.PublishEvent(\"message\", __rmsg.ToByteArray(), reliable);");
            sb.AppendLine("            });");
            sb.AppendLine("    }");

            sb.AppendLine("}");
            if (!string.IsNullOrEmpty(info.Namespace))
                sb.AppendLine("}");

            return sb.ToString();
        }
    }
}