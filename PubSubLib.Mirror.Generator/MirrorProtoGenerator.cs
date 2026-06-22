using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PubSubLib.Mirror.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class MirrorProtoGenerator : IIncrementalGenerator
{
    private const string MirrorProtoAttr = "PubSubLib.Mirror.MirrorProtoAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider.ForAttributeWithMetadataName(
            MirrorProtoAttr,
            predicate: static (node, _) => node is ClassDeclarationSyntax,
            transform: static (ctx, _) => TransformClass(ctx)
        );

        var valid = classes.Where(static c => c is not null);

        context.RegisterSourceOutput(valid, static (spc, info) => GenerateCode(spc, info!.Value));
    }

    private static MirrorClassInfo? TransformClass(GeneratorAttributeSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.TargetNode;
        var classSymbol = (INamedTypeSymbol)ctx.TargetSymbol;

        if (!classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            return null;

        var attr = ctx.Attributes[0];
        if (attr.ConstructorArguments.Length < 1 || attr.ConstructorArguments[0].Value is not INamedTypeSymbol protoType)
            return null;

        string? dataName = null;
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "DataName" && namedArg.Value.Value is string dn)
                dataName = dn;
        }
        dataName ??= protoType.Name;

        var ns = classSymbol.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .TrimStartGlobalPrefix() ?? "";

        var fields = new List<FieldMapping>();
        var reserved = new HashSet<string> { "PlayerId", "IsOnLine", "DataName" };

        foreach (var member in protoType.GetMembers())
        {
            if (member is not IPropertySymbol propSymbol) continue;
            if (propSymbol.IsStatic) continue;
            if (propSymbol.IsIndexer) continue;
            if (!propSymbol.ExplicitInterfaceImplementations.IsEmpty) continue;
            if (!SymbolEqualityComparer.Default.Equals(propSymbol.ContainingType, protoType)) continue;

            var propName = propSymbol.Name;
            if (reserved.Contains(propName)) continue;
            if (propName.EndsWith("Case") && propSymbol.Type.TypeKind == TypeKind.Enum) continue;

            var fieldName = ToFieldName(propName);
            var typeName = propSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .TrimStartGlobalPrefix();

            var isRepeated = false;
            var elementTypeName = typeName;
            if (propSymbol.Type is INamedTypeSymbol namedType && namedType.IsGenericType
                && namedType.MetadataName == "RepeatedField`1"
                && namedType.ContainingNamespace?.ToDisplayString() == "Google.Protobuf.Collections")
            {
                isRepeated = true;
                elementTypeName = namedType.TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .TrimStartGlobalPrefix();
            }

            fields.Add(new FieldMapping(fieldName, propName, typeName, isRepeated, elementTypeName, !propSymbol.Type.IsValueType));
        }

        var (structGroups, structGroupIndex) = DetectStructGroups(fields);
        for (int i = 0; i < fields.Count; i++)
        {
            if (structGroupIndex[i] >= 0)
            {
                var f = fields[i];
                fields[i] = new FieldMapping(f.FieldName, f.PropertyName, f.TypeName, f.IsRepeated, f.ElementTypeName, f.IsReferenceType, structGroupIndex[i]);
            }
        }

        var fullProtoName = protoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .TrimStartGlobalPrefix();

        return new MirrorClassInfo(ns, classSymbol.Name, fullProtoName, dataName, fields.ToArray(), structGroups);
    }

    private static string ToFieldName(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return "_";
        return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }

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

    internal static (StructGroup[] groups, int[] structGroupIndex) DetectStructGroups(List<FieldMapping> fields)
    {
        var candidates = new List<(int index, string structName, string fieldName)>();

        for (int i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            if (!f.IsRepeated) continue;

            var words = SplitPascalCase(f.PropertyName);
            int firstX = words.IndexOf("X");
            int lastX = words.LastIndexOf("X");
            if (firstX < 0 || lastX < 0 || firstX == lastX) continue;

            var structName = string.Concat(words.Skip(firstX + 1).Take(lastX - firstX - 1));
            var fieldName = string.Concat(words.Skip(lastX + 1));

            if (string.IsNullOrEmpty(structName) || string.IsNullOrEmpty(fieldName)) continue;

            candidates.Add((i, structName, fieldName));
        }

        var groups = new List<StructGroup>();
        var index = new int[fields.Count];
        for (int i = 0; i < fields.Count; i++) index[i] = -1;

        foreach (var grp in candidates.GroupBy(c => c.structName))
        {
            var members = grp.ToList();
            if (members.Count < 2) continue;

            var structName = grp.Key;
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

            groups.Add(new StructGroup(structName, fieldName, protoPropNames, fieldPropNames, elementTypes, isRefTypes));
        }

        return (groups.ToArray(), index);
    }

    private static void GenerateCode(SourceProductionContext context, MirrorClassInfo info)
    {
        if (info.Fields.Length == 0 && info.StructGroups.Length == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using Google.Protobuf;");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8618");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(info.Namespace))
        {
            sb.AppendLine($"namespace {info.Namespace}");
            sb.AppendLine("{");
        }
        sb.AppendLine($"partial class {info.ClassName} : global::PubSubLib.IPlayerData, global::PubSubLib.IPlayerDataInternal");
        sb.AppendLine("{");

        foreach (var sg in info.StructGroups)
        {
            sb.AppendLine();
            sb.AppendLine($"    public struct {sg.StructName}");
            sb.AppendLine("    {");
            for (int i = 0; i < sg.FieldPropNames.Length; i++)
            {
                sb.AppendLine($"        public {sg.ElementTypes[i]} {sg.FieldPropNames[i]} {{ get; }}");
            }
            sb.AppendLine();
            var ctorArgs = new List<string>();
            for (int i = 0; i < sg.FieldPropNames.Length; i++)
            {
                ctorArgs.Add($"{sg.ElementTypes[i]} {char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}");
            }
            sb.AppendLine($"        public {sg.StructName}({string.Join(", ", ctorArgs)})");
            sb.AppendLine("        {");
            for (int i = 0; i < sg.FieldPropNames.Length; i++)
            {
                var argName = $"{char.ToLowerInvariant(sg.FieldPropNames[i][0])}{sg.FieldPropNames[i].Substring(1)}";
                sb.AppendLine($"            {sg.FieldPropNames[i]} = {argName};");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }

        sb.AppendLine($"    private {info.ProtoTypeFullName}? _mirrorProto;");
        sb.AppendLine("    private Action<byte[], string>? _onChange;");
        sb.AppendLine();
        sb.AppendLine("    private long ___gs_playerId;");
        sb.AppendLine("    public long PlayerId => ___gs_playerId;");
        sb.AppendLine("    void global::PubSubLib.IPlayerDataInternal.SetPlayerId(long playerId) => ___gs_playerId = playerId;");
        sb.AppendLine();
        sb.AppendLine("    private bool ___gs_isOnLine;");
        sb.AppendLine("    public bool IsOnLine => ___gs_isOnLine;");
        sb.AppendLine("    void global::PubSubLib.IPlayerDataInternal.SetOnline(bool isOnline) => ___gs_isOnLine = isOnline;");
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

        foreach (var f in info.Fields)
        {
            if (f.StructGroupIndex >= 0) continue;

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

        var hasAnyField = info.Fields.Any(f => f.StructGroupIndex < 0);
        var hasAnyStructGroup = info.StructGroups.Length > 0;
        if (hasAnyField || hasAnyStructGroup)
        {
            sb.AppendLine("    public void Commit(string commit)");
            sb.AppendLine("    {");
            sb.AppendLine("        var proto = GetMirrorProto();");
            foreach (var sg in info.StructGroups)
            {
                sb.AppendLine($"        {sg.StructName}[]? ___arr_{sg.FieldName} = {sg.FieldName}.IsDirty ? {sg.FieldName}.ToArray() : null;");
                sb.AppendLine($"        {sg.FieldName}.ClearDirty();");
            }
            foreach (var f in info.Fields)
            {
                if (f.StructGroupIndex >= 0) continue;
                if (f.IsRepeated)
                {
                    sb.AppendLine($"        {f.ElementTypeName}[]? ___arr_{f.FieldName} = {f.FieldName}.IsDirty ? {f.FieldName}.ToArray() : null;");
                    sb.AppendLine($"        {f.FieldName}.ClearDirty();");
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
                sb.AppendLine($"                    foreach (var __item in ___arr_{sg.FieldName})");
                sb.AppendLine("                    {");
                for (int i = 0; i < sg.ProtoPropNames.Length; i++)
                {
                    sb.AppendLine($"                        __p.{sg.ProtoPropNames[i]}.Add(__item.{sg.FieldPropNames[i]});");
                }
                sb.AppendLine("                    }");
                sb.AppendLine("                }");
            }
            foreach (var f in info.Fields)
            {
                if (f.StructGroupIndex >= 0) continue;
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
                    sb.AppendLine($"                if ({f.FieldName} is not null) __p.{f.PropertyName} = {f.FieldName};");
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

        sb.AppendLine("}");
        if (!string.IsNullOrEmpty(info.Namespace))
            sb.AppendLine("}");

        context.AddSource($"{info.ClassName}.g.cs", sb.ToString());
    }
}

internal static class StringExtensions
{
    public static string TrimStartGlobalPrefix(this string s)
    {
        const string prefix = "global::";
        if (s.StartsWith(prefix))
            return s.Substring(prefix.Length);
        return s;
    }
}

internal readonly struct FieldMapping
{
    public readonly string FieldName;
    public readonly string PropertyName;
    public readonly string TypeName;
    public readonly bool IsRepeated;
    public readonly string ElementTypeName;
    public readonly bool IsReferenceType;
    public readonly int StructGroupIndex;
    public FieldMapping(string fieldName, string propertyName, string typeName, bool isRepeated, string elementTypeName, bool isReferenceType, int structGroupIndex = -1)
    {
        FieldName = fieldName;
        PropertyName = propertyName;
        TypeName = typeName;
        IsRepeated = isRepeated;
        ElementTypeName = elementTypeName;
        IsReferenceType = isReferenceType;
        StructGroupIndex = structGroupIndex;
    }
}

internal sealed class StructGroup
{
    public readonly string StructName;
    public readonly string FieldName;
    public readonly string[] ProtoPropNames;
    public readonly string[] FieldPropNames;
    public readonly string[] ElementTypes;
    public readonly bool[] IsRefTypes;
    public StructGroup(string structName, string fieldName, string[] protoPropNames, string[] fieldPropNames, string[] elementTypes, bool[] isRefTypes)
    {
        StructName = structName;
        FieldName = fieldName;
        ProtoPropNames = protoPropNames;
        FieldPropNames = fieldPropNames;
        ElementTypes = elementTypes;
        IsRefTypes = isRefTypes;
    }
}

internal readonly struct MirrorClassInfo
{
    public readonly string Namespace;
    public readonly string ClassName;
    public readonly string ProtoTypeFullName;
    public readonly string DataName;
    public readonly FieldMapping[] Fields;
    public readonly StructGroup[] StructGroups;
    public MirrorClassInfo(string ns, string className, string protoTypeFullName, string dataName, FieldMapping[] fields, StructGroup[] structGroups)
    {
        Namespace = ns;
        ClassName = className;
        ProtoTypeFullName = protoTypeFullName;
        DataName = dataName;
        Fields = fields;
        StructGroups = structGroups;
    }
}
