using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace PubSubLib.Mirror.Generator
{
    public static class UnitMirrorClientFileGenerator
    {
        private static readonly Dictionary<string, string> CSharpAliases = new()
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
                           $"'{typeClass.FullName}' khong hop le de sinh UnitMirrorClient " +
                           $"(thieu [UnitMirrorClientAttribute] hoac thieu ProtoType).");

            var source = BuildSource(info);

            var dir = Path.GetDirectoryName(pathOutPut);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(pathOutPut, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static UnitMirrorClassInfo? BuildClassInfo(Type typeClass)
        {
            var attr = typeClass.GetCustomAttribute<UnitMirrorClientAttribute>();
            if (attr is null) return null;

            var protoType = attr.ProtoType;
            if (protoType is null) return null;

            var unitType = attr.UnitType ?? protoType.Name;
            var targetTypeFullName = attr.Target is not null
                ? "global::" + GetFriendlyTypeName(attr.Target)
                : "global::PubSubLib.Client.IAlive";

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

            return new UnitMirrorClassInfo(ns, typeClass.Name, fullProtoName, unitType, targetTypeFullName,
                fields.ToArray(), structGroups, vectorGroups);
        }

        private static string ToFieldName(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return "_";
            return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }

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

        private static string BuildSource(UnitMirrorClassInfo info)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Google.Protobuf;");
            sb.AppendLine("using PubSubLib;");
            sb.AppendLine("using PubSubLib.Client;");
            sb.AppendLine("using PubSubLib.Contracts;");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS8618");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(info.Namespace))
            {
                sb.AppendLine($"namespace {info.Namespace}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"partial class {info.ClassName} : global::PubSubLib.Client.IRegionUnit<{info.TargetTypeFullName}>, global::PubSubLib.Client.IRegionClientUnitInternal");
            sb.AppendLine("{");

            sb.AppendLine($"    public static string _unitType = \"{info.UnitType}\";");
            sb.AppendLine($"    public string UnitType => _unitType;");
            sb.AppendLine();
            sb.AppendLine("    private long _id;");
            sb.AppendLine("    private global::PubSubLib.Vector2 _regionPosition;");
            sb.AppendLine("    private object _target;");
            sb.AppendLine();
            sb.AppendLine("    public long Id => _id;");
            sb.AppendLine();
            sb.AppendLine($"    public {info.TargetTypeFullName} GetTarget() => ({info.TargetTypeFullName})_target;");
            sb.AppendLine();
            sb.AppendLine("    void global::PubSubLib.Client.IRegionClientUnitInternal.Init(long id, global::PubSubLib.Vector2 position)");
            sb.AppendLine("    {");
            sb.AppendLine("        _id = id;");
            sb.AppendLine("        _regionPosition = position;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void global::PubSubLib.Client.IRegionClientUnitInternal.SetTarget(object target) => _target = target;");
            sb.AppendLine();

            sb.AppendLine($"    private {info.ProtoTypeFullName}? _mirrorProto;");
            sb.AppendLine("    private bool _initialized;");
            sb.AppendLine("    public bool IsInitialized => _initialized;");
            sb.AppendLine();
            // sb.AppendLine("    partial void OnCommit(string commit);");
            // sb.AppendLine("    partial void OnStart();");
            sb.AppendLine();
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
                var typeKeyword = sg.IsClass ? "class" : "struct";
                sb.AppendLine($"    public {typeKeyword} {sg.StructName}");
                sb.AppendLine("    {");
                for (int i = 0; i < sg.FieldPropNames.Length; i++)
                {
                    sb.AppendLine($"        public {sg.ElementTypes[i]} {sg.FieldPropNames[i]} {{ get; }}");
                }
                foreach (var vf in sg.Vector3Fields)
                {
                    if (vf.IsArray)
                    {
                        var bf = "_" + char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                        sb.AppendLine($"        private readonly System.Collections.Generic.List<global::PubSubLib.Vector3> {bf};");
                        sb.AppendLine($"        public System.Collections.Generic.IReadOnlyList<global::PubSubLib.Vector3> {vf.FieldName} => {bf};");
                    }
                    else
                    {
                        sb.AppendLine($"        public global::PubSubLib.Vector3 {vf.FieldName} {{ get; }}");
                    }
                }
                foreach (var pa in sg.PrimitiveArrayFields)
                {
                    var bf = "_" + char.ToLowerInvariant(pa.FieldName[0]) + pa.FieldName.Substring(1);
                    sb.AppendLine($"        private readonly System.Collections.Generic.List<{pa.ElementType}> {bf};");
                    sb.AppendLine($"        public System.Collections.Generic.IReadOnlyList<{pa.ElementType}> {pa.FieldName} => {bf};");
                }
                sb.AppendLine();
                var ctorArgs = new List<string>();
                for (int i = 0; i < sg.FieldPropNames.Length; i++)
                {
                    ctorArgs.Add($"{sg.ElementTypes[i]} {char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}");
                }
                foreach (var vf in sg.Vector3Fields)
                {
                    var argName = char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                    var v3Type = vf.IsArray ? "System.Collections.Generic.IReadOnlyList<global::PubSubLib.Vector3>" : "global::PubSubLib.Vector3";
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
                    var argName = $"{char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}";
                    sb.AppendLine($"            {sg.FieldPropNames[i]} = {argName};");
                }
                foreach (var vf in sg.Vector3Fields)
                {
                    var argName = char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                    if (vf.IsArray)
                    {
                        var bf = "_" + char.ToLowerInvariant(vf.FieldName[0]) + vf.FieldName.Substring(1);
                        sb.AppendLine($"            {bf} = new System.Collections.Generic.List<global::PubSubLib.Vector3>({argName});");
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
                    sb.AppendLine($"            {bf} = new System.Collections.Generic.List<{pa.ElementType}>({argName});");
                }
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }

            foreach (var f in info.Fields)
            {
                if (f.StructGroupIndex >= 0) continue;
                if (f.VectorGroupIndex >= 0) continue;
                if (f.IsRepeated)
                {
                    var fieldType = $"System.Collections.Generic.List<{f.ElementTypeName}>";
                    var propType = $"System.Collections.Generic.IReadOnlyList<{f.ElementTypeName}>";
                    sb.AppendLine($"    private readonly {fieldType} {f.FieldName} = new();");
                    sb.AppendLine();
                    sb.AppendLine($"    public {propType} {f.PropertyName} => {f.FieldName};");
                }
                else
                {
                    sb.AppendLine($"    private {f.TypeName} {f.FieldName};");
                    sb.AppendLine();
                    sb.AppendLine($"    public {f.TypeName} {f.PropertyName}");
                    sb.AppendLine("    {");
                    sb.AppendLine($"        get => {f.FieldName};");
                    sb.AppendLine("    }");
                }
                sb.AppendLine();
            }

            foreach (var sg in info.StructGroups)
            {
                var fieldType = $"System.Collections.Generic.List<{sg.StructName}>";
                var propType = $"System.Collections.Generic.IReadOnlyList<{sg.StructName}>";
                sb.AppendLine($"    private readonly {fieldType} {sg.FieldName} = new();");
                sb.AppendLine();
                sb.AppendLine($"    public {propType} {sg.StructName}s => {sg.FieldName};");
                sb.AppendLine();
            }

            foreach (var vg in info.VectorGroups)
            {
                if (vg.IsList)
                {
                    var fieldType = "System.Collections.Generic.List<global::PubSubLib.Vector3>";
                    var propType = "System.Collections.Generic.IReadOnlyList<global::PubSubLib.Vector3>";
                    sb.AppendLine($"    private readonly {fieldType} {vg.FieldName} = new();");
                    sb.AppendLine();
                    sb.AppendLine($"    public {propType} {vg.PropertyName} => {vg.FieldName};");
                }
                else
                {
                    sb.AppendLine($"    private global::PubSubLib.Vector3 {vg.FieldName};");
                    sb.AppendLine();
                    sb.AppendLine($"    public global::PubSubLib.Vector3 {vg.PropertyName} => {vg.FieldName};");
                }
                sb.AppendLine();
            }

            sb.AppendLine("    public void SyncFromProto()");
            sb.AppendLine("    {");
            sb.AppendLine("        var proto = GetMirrorProto();");
            foreach (var sg in info.StructGroups)
            {
                sb.AppendLine($"        {sg.FieldName}.Clear();");
                foreach (var vf in sg.Vector3Fields)
                {
                    if (vf.IsArray)
                        sb.AppendLine($"        int ___vecOff_{vf.FieldName} = 0;");
                }
                foreach (var pa in sg.PrimitiveArrayFields)
                {
                    sb.AppendLine($"        int ___arrOff_{pa.FieldName} = 0;");
                }
                if (sg.ProtoPropNames.Length > 0)
                    sb.AppendLine($"        int __count = proto.{sg.ProtoPropNames[0]}.Count;");
                else if (sg.PrimitiveArrayFields.Length > 0)
                    sb.AppendLine($"        int __count = proto.{sg.PrimitiveArrayFields[0].CountProtoName}.Count;");
                else
                    sb.AppendLine($"        int __count = proto.{sg.Vector3Fields[0].CountProtoName ?? sg.Vector3Fields[0].ValueProtoName}.Count / 3;");
                sb.AppendLine("        for (int __i = 0; __i < __count; __i++)");
                sb.AppendLine("        {");
                var ctorArgs = new List<string>();
                for (int i = 0; i < sg.ProtoPropNames.Length; i++)
                {
                    ctorArgs.Add($"__i < proto.{sg.ProtoPropNames[i]}.Count ? proto.{sg.ProtoPropNames[i]}[__i] : default");
                }
                foreach (var vf in sg.Vector3Fields)
                {
                    var accField = $"___vecAcc_{vf.FieldName}";
                    if (vf.IsArray)
                    {
                        sb.AppendLine($"            int ___vecCnt_{vf.FieldName} = __i < proto.{vf.CountProtoName}.Count ? proto.{vf.CountProtoName}[__i] : 0;");
                        sb.AppendLine($"            var {accField} = new global::PubSubLib.Vector3[___vecCnt_{vf.FieldName}];");
                        sb.AppendLine($"            for (int __j = 0; __j < ___vecCnt_{vf.FieldName}; __j++)");
                        sb.AppendLine("            {");
                        sb.AppendLine($"                int __b = ___vecOff_{vf.FieldName} + __j * 3;");
                        sb.AppendLine($"                {accField}[__j] = new global::PubSubLib.Vector3");
                        sb.AppendLine("                {");
                        sb.AppendLine($"                    x = __b < proto.{vf.ValueProtoName}.Count ? proto.{vf.ValueProtoName}[__b] : 0f,");
                        sb.AppendLine($"                    y = __b + 1 < proto.{vf.ValueProtoName}.Count ? proto.{vf.ValueProtoName}[__b + 1] : 0f,");
                        sb.AppendLine($"                    z = __b + 2 < proto.{vf.ValueProtoName}.Count ? proto.{vf.ValueProtoName}[__b + 2] : 0f");
                        sb.AppendLine("                };");
                        sb.AppendLine("            }");
                        sb.AppendLine($"            ___vecOff_{vf.FieldName} += ___vecCnt_{vf.FieldName} * 3;");
                        ctorArgs.Add(accField);
                    }
                    else
                    {
                        sb.AppendLine($"            int __b = __i * 3;");
                        sb.AppendLine($"            var {accField} = __b + 2 < proto.{vf.ValueProtoName}.Count");
                        sb.AppendLine("                ? new global::PubSubLib.Vector3");
                        sb.AppendLine("                {");
                        sb.AppendLine($"                    x = proto.{vf.ValueProtoName}[__b],");
                        sb.AppendLine($"                    y = proto.{vf.ValueProtoName}[__b + 1],");
                        sb.AppendLine($"                    z = proto.{vf.ValueProtoName}[__b + 2]");
                        sb.AppendLine("                }");
                        sb.AppendLine("                : default;");
                        ctorArgs.Add(accField);
                    }
                }
                foreach (var pa in sg.PrimitiveArrayFields)
                {
                    var accField = $"___arrAcc_{pa.FieldName}";
                    sb.AppendLine($"            int ___arrCnt_{pa.FieldName} = __i < proto.{pa.CountProtoName}.Count ? proto.{pa.CountProtoName}[__i] : 0;");
                    sb.AppendLine($"            var {accField} = new {pa.ElementType}[___arrCnt_{pa.FieldName}];");
                    sb.AppendLine($"            for (int __j = 0; __j < ___arrCnt_{pa.FieldName}; __j++)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                int __b = ___arrOff_{pa.FieldName} + __j;");
                    sb.AppendLine($"                {accField}[__j] = __b < proto.{pa.ValueProtoName}.Count ? proto.{pa.ValueProtoName}[__b] : default;");
                    sb.AppendLine("            }");
                    sb.AppendLine($"            ___arrOff_{pa.FieldName} += ___arrCnt_{pa.FieldName};");
                    ctorArgs.Add(accField);
                }
                sb.AppendLine($"            {sg.FieldName}.Add(new {sg.StructName}({string.Join(", ", ctorArgs)}));");
                sb.AppendLine("        }");
            }
            foreach (var vg in info.VectorGroups)
            {
                if (vg.IsList)
                {
                    sb.AppendLine($"        {vg.FieldName}.Clear();");
                    sb.AppendLine($"        int __count_{vg.FieldName} = proto.{vg.ProtoPropName}.Count / 3;");
                    sb.AppendLine($"        for (int __i = 0; __i < __count_{vg.FieldName}; __i++)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            int __b = __i * 3;");
                    sb.AppendLine($"            {vg.FieldName}.Add(new global::PubSubLib.Vector3");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                x = proto.{vg.ProtoPropName}[__b],");
                    sb.AppendLine($"                y = proto.{vg.ProtoPropName}[__b + 1],");
                    sb.AppendLine($"                z = proto.{vg.ProtoPropName}[__b + 2]");
                    sb.AppendLine("            });");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        if (proto.{vg.ProtoPropName}.Count >= 3)");
                    sb.AppendLine($"            {vg.FieldName} = new global::PubSubLib.Vector3");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                x = proto.{vg.ProtoPropName}[0],");
                    sb.AppendLine($"                y = proto.{vg.ProtoPropName}[1],");
                    sb.AppendLine($"                z = proto.{vg.ProtoPropName}[2]");
                    sb.AppendLine("            };");
                }
            }
            foreach (var f in info.Fields)
            {
                if (f.StructGroupIndex >= 0) continue;
                if (f.VectorGroupIndex >= 0) continue;
                if (f.IsRepeated)
                {
                    sb.AppendLine($"        {f.FieldName}.Clear();");
                    sb.AppendLine($"        {f.FieldName}.AddRange(proto.{f.PropertyName});");
                }
                else
                {
                    sb.AppendLine($"        {f.FieldName} = proto.{f.PropertyName};");
                }
            }
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public void ApplyUpdate(byte[] mirrorData, string commit)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var proto = {info.ProtoTypeFullName}.Parser.ParseFrom(mirrorData);");
            sb.AppendLine("        _mirrorProto = proto;");
            sb.AppendLine("        SyncFromProto();");
            sb.AppendLine("        if (!_initialized)");
            sb.AppendLine("        {");
            sb.AppendLine("            _initialized = true;");
            sb.AppendLine("            if (this is IOnStart os) os.OnStart();");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine("            if (this is IOnCommit co) co.OnCommit(commit);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    private readonly Dictionary<string, List<Action<byte[]>>> _msgHandlers = new();");
            sb.AppendLine();
            sb.AppendLine("    public MyConnection.ISubscribe OnMessage<T>(string subject, Action<T> callback)");
            sb.AppendLine("        where T : class, global::Google.Protobuf.IMessage<T>, new()");
            sb.AppendLine("    {");
            sb.AppendLine("        var parser = new global::Google.Protobuf.MessageParser<T>(() => new T());");
            sb.AppendLine("        void handler(byte[] bytes) { callback(parser.ParseFrom(bytes)); }");
            sb.AppendLine("        if (!_msgHandlers.TryGetValue(subject, out var list))");
            sb.AppendLine("            _msgHandlers[subject] = list = new List<Action<byte[]>>();");
            sb.AppendLine("        list.Add(handler);");
            sb.AppendLine("        return new __RegionMsgSub(() => list.Remove(handler));");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void global::PubSubLib.Client.IRegionClientUnitInternal.DispatchMessage(string subject, byte[] data)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_msgHandlers.TryGetValue(subject, out var list))");
            sb.AppendLine("            foreach (var h in list)");
            sb.AppendLine("            {");
            sb.AppendLine("                try { h(data); }");
            sb.AppendLine("                catch (Exception ex) { PubSubLib.Contracts.PubSubLog.Error(ex, \"DispatchMessage failed\"); }");
            sb.AppendLine("            }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    void global::PubSubLib.Client.IRegionClientUnitInternal.DisposeMessageSubs()");
            sb.AppendLine("    {");
            sb.AppendLine("        _msgHandlers.Clear();");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.AppendLine("    private sealed class __RegionMsgSub : MyConnection.ISubscribe");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly Action _unsubscribe;");
            sb.AppendLine("        public void UnSubscribe() => _unsubscribe();");
            sb.AppendLine("        public __RegionMsgSub(Action unsubscribe) => _unsubscribe = unsubscribe;");
            sb.AppendLine("    }");

            sb.AppendLine("}");
            if (!string.IsNullOrEmpty(info.Namespace))
                sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
