using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PubSubLib.Mirror.Generator
{
    /// <summary>
    /// Bản "runtime" của MirrorProtoGenerator: đọc metadata bằng System.Reflection thay vì
    /// Roslyn ISymbol, rồi ghi thẳng ra file .cs trên đĩa thay vì context.AddSource.
    ///
    /// LƯU Ý QUAN TRỌNG:
    /// FieldMapping / StructGroup / VectorGroup / MirrorClassInfo / DetectStructGroups / DetectVectorGroups
    /// trong file gốc là `internal` và nằm trong assembly của Source Generator (thường target
    /// netstandard2.0, chỉ được add vào project game dưới dạng &lt;Analyzer&gt;, KHÔNG
    /// ReferencesOutputAssembly). Nghĩa là code runtime của bạn (nơi gọi GenerateFile) thường
    /// KHÔNG nhìn thấy các type này nếu chỉ reference generator theo kiểu analyzer.
    /// => Muốn dùng lại UnitMirrorFileGenerator.cs + MirrorProtoFileGenerator.cs như một tool
    ///    riêng (console app / MSBuild task chạy trước build), bạn cần:
    ///    (a) reference project generator theo kiểu ProjectReference bình thường (không phải
    ///        OutputItemType="Analyzer"), hoặc
    ///    (b) copy 4 type (FieldMapping, StructGroup, VectorGroup, MirrorClassInfo) + 2 hàm
    ///        Detect* sang 1 file dùng chung cho cả generator lẫn tool này.
    ///
    /// Giả định về attribute (chỉnh lại nếu khác thật):
    /// - MirrorProtoAttribute có: ProtoType (Type, ctor arg), DataName (string?, named arg)
    /// </summary>
    public static class MirrorProtoFileGenerator
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

        /// <returns>true nếu có file được ghi ra; false nếu class không có field nào để mirror (giống early-return của generator gốc)</returns>
        public static bool GenerateFile(Type typeClass, string pathOutPut)
        {
            var info = BuildClassInfo(typeClass)
                       ?? throw new InvalidOperationException(
                           $"'{typeClass.FullName}' không hợp lệ để sinh MirrorProto " +
                           $"(thiếu [MirrorProtoAttribute] hoặc thiếu ProtoType).");

            // giống hệt: if (info.Fields.Length == 0 && info.StructGroups.Length == 0) return;
            if (info.Fields.Length == 0 && info.StructGroups.Length == 0)
                return false;

            var source = BuildSource(info);

            var dir = Path.GetDirectoryName(pathOutPut);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(pathOutPut, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }

        // ================= Bước 1: đọc metadata bằng Reflection (thay TransformClass) =================

        private static MirrorClassInfo? BuildClassInfo(Type typeClass)
        {
            var attr = typeClass.GetCustomAttribute<MirrorProtoAttribute>();
            if (attr is null) return null;

            var protoType = attr.ProtoType;
            if (protoType is null) return null;

            var dataName = attr.DataName ?? protoType.Name;

            var ns = typeClass.Namespace ?? "";

            var fields = new List<FieldMapping>();
            var reserved = new HashSet<string> { "PlayerId", "IsOnLine", "DataName" };

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

            var fullProtoName = "global::" + GetFriendlyTypeName(protoType);

            return new MirrorClassInfo(ns, typeClass.Name, fullProtoName, dataName, fields.ToArray(), structGroups,
                vectorGroups);
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

        private static string BuildSource(MirrorClassInfo info)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Google.Protobuf;");
            sb.AppendLine("using PubSubLib.Contracts;");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS8618");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
            }

            sb.AppendLine(
                $"partial class {info.ClassName} : global::PubSubLib.IPlayerData, global::PubSubLib.IPlayerDataInternal");
            sb.AppendLine("{");

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

            sb.AppendLine($"    private {info.ProtoTypeFullName}? _mirrorProto;");
            sb.AppendLine("    private Action<byte[], string>? _onChange;");
            sb.AppendLine("    private Action<string, byte[]>? _onMessage;");
            sb.AppendLine(
                "    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<__IMsgHandler>> _msgHandlers = new();");
            sb.AppendLine();
            sb.AppendLine("    private long ___gs_playerId;");
            sb.AppendLine("    public long PlayerId => ___gs_playerId;");
            sb.AppendLine("    void global::PubSubLib.IPlayerDataInternal.SetPlayerId(long playerId)");
            sb.AppendLine("    {");
            sb.AppendLine("        ___gs_playerId = playerId;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private bool ___gs_isOnLine;");
            sb.AppendLine("    public bool IsOnLine => ___gs_isOnLine;");
            sb.AppendLine(
                "    void global::PubSubLib.IPlayerDataInternal.SetOnline(bool isOnline) => ___gs_isOnLine = isOnline;");
            sb.AppendLine();
            sb.AppendLine("    private bool ___gs_initDone;");
            sb.AppendLine("    bool global::PubSubLib.IPlayerDataInternal.IsInitDone => ___gs_initDone;");
            sb.AppendLine("    public void DoneInit()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (___gs_initDone) return;");
            sb.AppendLine("        ___gs_initDone = true;");
            sb.AppendLine("        if (this is global::PubSubLib.IOnCreate onCreate)");
            sb.AppendLine("        {");
            sb.AppendLine("            try { onCreate.OnCreate(); }");
            sb.AppendLine("            catch (Exception ex) { PubSubLog.Error(ex, \"IOnCreate.OnCreate failed\"); }");
            sb.AppendLine("        }");
            sb.AppendLine("        if (___gs_isOnLine && this is global::PubSubLib.IOnClientConnect onConnect)");
            sb.AppendLine("        {");
            sb.AppendLine("            try { onConnect.OnClientConnect(); }");
            sb.AppendLine("            catch (Exception ex) { PubSubLog.Error(ex, \"IOnClientConnect failed\"); }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    public string DataName => \"{info.DataName}\";");
            sb.AppendLine();
            sb.AppendLine($"    public {info.ProtoTypeFullName} GetMirrorProto()");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_mirrorProto is null)");
            sb.AppendLine($"            _mirrorProto = new {info.ProtoTypeFullName}();");
            sb.AppendLine("        return _mirrorProto;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public void OnChange(Action<byte[], string> handler)");
            sb.AppendLine("    {");
            sb.AppendLine("        _onChange += handler;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void global::PubSubLib.IPlayerData.OnMessage(Action<string, byte[]> handler)");
            sb.AppendLine("    {");
            sb.AppendLine("        _onMessage += handler;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine(
                "    public void SendMessage<T>(string subject, T data) where T : class, global::Google.Protobuf.IMessage");
            sb.AppendLine("    {");
            sb.AppendLine("        global::PubSubLib.Mirror.MirrorProtoBus.EnqueueMessage(subject, data, _onMessage);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public System.IDisposable OnMessage<T>(string subject, System.Action<T> callback)");
            sb.AppendLine("        where T : class, global::Google.Protobuf.IMessage<T>, new()");
            sb.AppendLine("    {");
            sb.AppendLine("        var handler = new __MsgHandler<T>(callback);");
            sb.AppendLine("        if (!_msgHandlers.TryGetValue(subject, out var list))");
            sb.AppendLine(
                "            _msgHandlers[subject] = list = new System.Collections.Generic.List<__IMsgHandler>();");
            sb.AppendLine("        list.Add(handler);");
            sb.AppendLine("        return new __MessageSubscription(() => list.Remove(handler));");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine(
                "    System.Collections.Generic.List<System.Action> global::PubSubLib.IPlayerDataInternal.PrepareMessageDispatch(string subject, byte[] data)");
            sb.AppendLine("    {");
            sb.AppendLine("        var sink = new System.Collections.Generic.List<System.Action>();");
            sb.AppendLine("        if (_msgHandlers.TryGetValue(subject, out var list))");
            sb.AppendLine("            foreach (var h in list)");
            sb.AppendLine("            {");
            sb.AppendLine("                try { h.Prepare(data, sink); }");
            sb.AppendLine(
                "                catch (Exception ex) { PubSubLog.Error(ex, \"PrepareMessageDispatch handler failed\"); }");
            sb.AppendLine("            }");
            sb.AppendLine("        return sink;");
            sb.AppendLine("    }");
            sb.AppendLine();

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
                    {
                        sb.AppendLine(
                            $"        {f.ElementTypeName}[]? ___arr_{f.FieldName} = {f.FieldName}.TrySnapshot();");
                    }
                }

                sb.AppendLine("        global::PubSubLib.Mirror.MirrorProtoBus.Enqueue(proto,");
                sb.AppendLine("            __bytes => _onChange?.Invoke(__bytes, commit),");
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

            sb.AppendLine();
            sb.AppendLine("    private interface __IMsgHandler");
            sb.AppendLine("    {");
            sb.AppendLine("        void Prepare(byte[] data, System.Collections.Generic.List<System.Action> sink);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine(
                "    private sealed class __MsgHandler<T> : __IMsgHandler where T : class, global::Google.Protobuf.IMessage<T>, new()");
            sb.AppendLine("    {");
            sb.AppendLine(
                "        private readonly global::Google.Protobuf.MessageParser<T> _parser = new(() => new T());");
            sb.AppendLine("        private readonly System.Action<T> _callback;");
            sb.AppendLine("        public __MsgHandler(System.Action<T> callback) => _callback = callback;");
            sb.AppendLine(
                "        public void Prepare(byte[] data, System.Collections.Generic.List<System.Action> sink)");
            sb.AppendLine("        {");
            sb.AppendLine("            var obj = _parser.ParseFrom(data);");
            sb.AppendLine("            sink.Add(() => _callback(obj));");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private sealed class __MessageSubscription : System.IDisposable");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly System.Action _unsubscribe;");
            sb.AppendLine("        public void Dispose() => _unsubscribe();");
            sb.AppendLine(
                "        public __MessageSubscription(System.Action unsubscribe) => _unsubscribe = unsubscribe;");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            if (!string.IsNullOrEmpty(info.Namespace))
                sb.AppendLine("}");

            return sb.ToString();
        }
    }
}