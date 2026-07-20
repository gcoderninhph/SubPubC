using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PubSubLib.Mirror.Generator
{
    public static class AttributeClassScanner
    {
        private const string GeneratedFolderRelativePath = "Assets/Generated";

        [MenuItem("Tools/Scan Client")]
        public static void ScanAttributeClasses()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var outputFolder = Path.Combine(projectRoot, GeneratedFolderRelativePath);
            Directory.CreateDirectory(outputFolder);

            int generatedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            var mirrorTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => t.GetCustomAttribute<MirrorProtoClientAttribute>() != null);

            foreach (var type in mirrorTypes)
            {
                var outPath = Path.Combine(outputFolder, $"{type.Name}.g.cs");
                try
                {
                    bool written = MirrorProtoClientFileGenerator.GenerateFile(type, outPath);
                    if (written)
                    {
                        generatedCount++;
                        Debug.Log($"[AttributeClassScanner] [MirrorProtoClient] Da sinh: {outPath}");
                    }
                    else
                    {
                        skippedCount++;
                        Debug.Log($"[AttributeClassScanner] [MirrorProtoClient] Bo qua '{type.Name}' (khong co field nao de mirror)");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogError($"[AttributeClassScanner] [MirrorProtoClient] Loi khi sinh file cho '{type.FullName}': {ex}");
                }
            }

            var unitTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => t.GetCustomAttribute<UnitMirrorClientAttribute>() != null);

            foreach (var type in unitTypes)
            {
                var outPath = Path.Combine(outputFolder, $"{type.Name}.UnitMirrorClient.g.cs");
                try
                {
                    UnitMirrorClientFileGenerator.GenerateFile(type, outPath);
                    generatedCount++;
                    Debug.Log($"[AttributeClassScanner] [UnitMirrorClient] Da sinh: {outPath}");
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.LogError($"[AttributeClassScanner] [UnitMirrorClient] Loi khi sinh file cho '{type.FullName}': {ex}");
                }
            }

            AssetDatabase.Refresh();

            Debug.Log($"[AttributeClassScanner] Hoan tat: {generatedCount} file duoc sinh, {skippedCount} bi bo qua, {errorCount} loi.");
        }
    }
}
