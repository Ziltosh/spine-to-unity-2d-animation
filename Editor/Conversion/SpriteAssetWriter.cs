using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;

namespace SpineTo2DAnimation.Editor.Conversion
{
    /// <summary>Sprite bone expressed in sprite rect pixels (origin at the bottom-left of the image).</summary>
    public struct SpriteBoneSpec
    {
        public string Name;
        public Vector2 Position;
        public float RotationDegrees;
        public float Length;
    }

    /// <summary>Custom sprite geometry with up to four bone influences per vertex.</summary>
    public sealed class SpriteMeshSpec
    {
        public Vector2[] Vertices;
        public BoneWeight[] Weights;
        public int[] Indices;
        public Vector2Int[] Edges;
        public List<SpriteBoneSpec> Bones = new List<SpriteBoneSpec>();
    }

    /// <summary>Writes extracted pixels as sprite assets and stores 2D Animation mesh and bone data on them.</summary>
    public static class SpriteAssetWriter
    {
        public static Sprite WriteSprite(
            string assetPath,
            Color32[] pixels,
            int width,
            int height,
            float pixelsPerUnit,
            SpriteMeshSpec mesh)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(ToAbsolutePath(assetPath), texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Compressed;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            if (mesh != null)
                ApplyMesh(importer, mesh);

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
                throw new InvalidOperationException($"Sprite could not be loaded after import: {assetPath}");

            return sprite;
        }

        private static void ApplyMesh(TextureImporter importer, SpriteMeshSpec mesh)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            ISpriteEditorDataProvider dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);
            dataProvider.InitSpriteEditorDataProvider();

            SpriteRect[] rects = dataProvider.GetSpriteRects();
            if (rects.Length != 1)
                throw new InvalidOperationException($"Expected a single sprite rect, found {rects.Length}.");

            GUID spriteId = rects[0].spriteID;

            var vertices = new Vertex2DMetaData[mesh.Vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
                vertices[index] = new Vertex2DMetaData { position = mesh.Vertices[index], boneWeight = mesh.Weights[index] };

            ISpriteMeshDataProvider meshProvider = dataProvider.GetDataProvider<ISpriteMeshDataProvider>();
            meshProvider.SetVertices(spriteId, vertices);
            meshProvider.SetIndices(spriteId, mesh.Indices);
            meshProvider.SetEdges(spriteId, mesh.Edges);

            var bones = new List<SpriteBone>(mesh.Bones.Count);
            foreach (SpriteBoneSpec bone in mesh.Bones)
            {
                bones.Add(new SpriteBone
                {
                    name = bone.Name,
                    guid = StableGuid(bone.Name),
                    position = bone.Position,
                    rotation = Quaternion.Euler(0f, 0f, bone.RotationDegrees),
                    length = Mathf.Max(1f, bone.Length),
                    parentId = -1,
                    color = Color.white
                });
            }

            dataProvider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(spriteId, bones);
            dataProvider.Apply();
            importer.SaveAndReimport();
        }

        /// <summary>Deterministic bone GUID so that reconverting an export rewrites identical import settings.</summary>
        private static string StableGuid(string name)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name ?? string.Empty));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unity project root not found.");
            return Path.Combine(projectRoot, assetPath);
        }
    }
}
