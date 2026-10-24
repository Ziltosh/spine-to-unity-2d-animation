using System;
using System.Globalization;
using System.IO;
using SpineTo2DAnimation.Editor.Conversion;
using UnityEditor;
using UnityEngine;

namespace SpineTo2DAnimation.Editor
{
    /// <summary>Command-line and menu entry points of the converter.</summary>
    public static class SpineConverterBatch
    {
        /// <summary>
        /// Batch entry point:
        /// <c>-executeMethod SpineTo2DAnimation.Editor.SpineConverterBatch.Convert -spineJson &lt;file&gt; -spineAtlas &lt;file&gt; -output Assets/... [-name N] [-ppu 100] [-fps 30]</c>.
        /// Throws on failure so that the batch process exits with an error code.
        /// </summary>
        public static void Convert()
        {
            var request = new SpineConversionRequest
            {
                SkeletonJsonPath = RequireArgument("-spineJson"),
                AtlasPath = RequireArgument("-spineAtlas"),
                OutputFolder = RequireArgument("-output"),
                Name = ReadArgument("-name")
            };

            string ppu = ReadArgument("-ppu");
            if (ppu != null)
                request.PixelsPerUnit = float.Parse(ppu, CultureInfo.InvariantCulture);

            string fps = ReadArgument("-fps");
            if (fps != null)
                request.FrameRate = float.Parse(fps, CultureInfo.InvariantCulture);

            SpineConversionResult result = SpineConverter.Convert(request);
            Debug.Log($"SpineTo2DAnimation: converted '{request.SkeletonJsonPath}' to {result.PrefabPath} ({result.BoneCount} bones, {result.RegionAttachmentCount} regions, {result.MeshAttachmentCount} meshes, {result.ClipPaths.Count} clips). Report: {result.ReportPath}");
        }

        [MenuItem("Tools/Spine to 2D Animation/Convert Spine Export...")]
        private static void ConvertFromMenu()
        {
            string json = EditorUtility.OpenFilePanel("Spine skeleton JSON", string.Empty, "json");
            if (string.IsNullOrEmpty(json))
                return;

            string atlas = EditorUtility.OpenFilePanel("Spine atlas", Path.GetDirectoryName(json), "txt,atlas");
            if (string.IsNullOrEmpty(atlas))
                return;

            string output = EditorUtility.SaveFolderPanel("Output folder (inside Assets)", Application.dataPath, Path.GetFileNameWithoutExtension(json));
            if (string.IsNullOrEmpty(output))
                return;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string relative = Path.GetFullPath(output).Substring(Path.GetFullPath(projectRoot).Length).TrimStart('\\', '/').Replace('\\', '/');
            SpineConversionResult result = SpineConverter.Convert(new SpineConversionRequest
            {
                SkeletonJsonPath = json,
                AtlasPath = atlas,
                OutputFolder = relative
            });

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath));
        }

        private static string RequireArgument(string name)
        {
            return ReadArgument(name) ?? throw new ArgumentException($"Missing command-line argument {name}.");
        }

        private static string ReadArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == name)
                    return arguments[index + 1];
            }

            return null;
        }
    }
}
