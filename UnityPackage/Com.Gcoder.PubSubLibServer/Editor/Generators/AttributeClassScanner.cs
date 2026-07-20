using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PubSubLib.Mirror.Generator
{
    /// <summary>
    /// Tools > Scan
    /// Quét toàn bộ Type đã compile có gắn [MirrorProto] hoặc [UnitMirrorServer],
    /// rồi đưa từng Type cho generator tương ứng để sinh file .g.cs vào Assets/Generated/&lt;TênClass&gt;.g.cs.
    ///
    /// Dùng UnityEditor.TypeCache thay vì tự quét text file .cs: nó trả về Type thật
    /// (cần thiết để gọi MirrorProtoFileGenerator/UnitMirrorFileGenerator, vốn nhận System.Type),
    /// và được Unity cache sẵn nên rất nhanh, không phải tự Reflect toàn bộ AppDomain.
    ///
    /// LƯU Ý: file này phải nằm trong 1 thư mục tên "Editor" (bất kỳ đâu trong Assets).
    /// </summary>
    public static class AttributeClassScanner
    {
        private const string GeneratedFolderRelativePath = "Assets/Generated";
 
        [MenuItem("Tools/Scan")]
        public static void ScanAttributeClasses()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var outputFolder = Path.Combine(projectRoot, GeneratedFolderRelativePath);
            Directory.CreateDirectory(outputFolder);
 
            int generatedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;
 
            // ---- [MirrorProto] -> MirrorProtoFileGenerator ----
            foreach (var type in TypeCache.GetTypesWithAttribute<MirrorProtoAttribute>())
            {
                var outPath = Path.Combine(outputFolder, $"{type.Name}.g.cs");
                try
                {
                    bool written = MirrorProtoFileGenerator.GenerateFile(type, outPath);
                    if (written)
                    {
                        generatedCount++;
                        Debug.Log($"[AttributeClassScanner] [MirrorProto] Đã sinh: {outPath}");
                    }
                    else
                    {
                        skippedCount++;
                        Debug.Log($"[AttributeClassScanner] [MirrorProto] Bỏ qua '{type.Name}' (không có field nào để mirror)");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogError($"[AttributeClassScanner] [MirrorProto] Lỗi khi sinh file cho '{type.FullName}': {ex}");
                }
            }
 
            // ---- [UnitMirrorServer] -> UnitMirrorFileGenerator ----
            foreach (var type in TypeCache.GetTypesWithAttribute<UnitMirrorServerAttribute>())
            {
                var outPath = Path.Combine(outputFolder, $"{type.Name}.g.cs");
                try
                {
                    UnitMirrorFileGenerator.GenerateFile(type, outPath);
                    generatedCount++;
                    Debug.Log($"[AttributeClassScanner] [UnitMirrorServer] Đã sinh: {outPath}");
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogError($"[AttributeClassScanner] [UnitMirrorServer] Lỗi khi sinh file cho '{type.FullName}': {ex}");
                }
            }
 
            AssetDatabase.Refresh();
 
            Debug.Log($"[AttributeClassScanner] Hoàn tất: {generatedCount} file được sinh, {skippedCount} bị bỏ qua, {errorCount} lỗi.");
        }
    }
}