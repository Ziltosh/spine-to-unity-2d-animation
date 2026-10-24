using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpineTo2DAnimation.Editor.Conversion;
using SpineTo2DAnimation.Editor.Reading;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace SpineTo2DAnimation.Editor.Tests
{
    public sealed class ConversionTests
    {
        private const string OutputFolder = "Assets/SpineTo2DAnimationTests_Output";
        private string sourceDirectory;

        [SetUp]
        public void SetUp()
        {
            sourceDirectory = Path.Combine(Path.GetTempPath(), "SpineTo2DAnimationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sourceDirectory);
            File.WriteAllText(Path.Combine(sourceDirectory, "fixture.json"), TestFixtures.SkeletonJson);
            File.WriteAllText(Path.Combine(sourceDirectory, "fixture.atlas.txt"), TestFixtures.AtlasText);
            WritePage(Path.Combine(sourceDirectory, "fixture.png"));
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(OutputFolder);
            if (Directory.Exists(sourceDirectory))
                Directory.Delete(sourceDirectory, true);
        }

        [Test]
        public void Convert_SyntheticExport_BuildsBonesRegionsAndSkinnedMesh()
        {
            SpineConversionResult result = Convert();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Transform arm = prefab.transform.Find("root/arm");
            Assert.That(arm, Is.Not.Null);
            Assert.That(arm.localPosition.x, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(arm.localEulerAngles.z, Is.EqualTo(90f).Within(1e-3f));

            Transform body = prefab.transform.Find("root/body : quad");
            Assert.That(body, Is.Not.Null);
            Assert.That(body.GetComponent<SpriteRenderer>().sprite, Is.Not.Null);
            Assert.That(body.GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(0));
            Assert.That(body.localPosition.y, Is.EqualTo(0.04f).Within(1e-5f));

            Transform mesh = prefab.transform.Find("Meshes/skin : quad");
            Assert.That(mesh, Is.Not.Null);
            SpriteRenderer meshRenderer = mesh.GetComponent<SpriteRenderer>();
            Assert.That(meshRenderer.sortingOrder, Is.EqualTo(1));
            Assert.That(meshRenderer.sprite.GetBones().Length, Is.EqualTo(2));
            SpriteSkin skin = mesh.GetComponent<SpriteSkin>();
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.boneTransforms.Length, Is.EqualTo(2));
            Assert.That(skin.boneTransforms[1], Is.SameAs(arm));

            Assert.That(result.MeshAttachmentCount, Is.EqualTo(1));
            Assert.That(result.RegionAttachmentCount, Is.EqualTo(1));
            Assert.That(result.Notes, Has.Some.Contains("fit scale 1"));
            Assert.That(prefab.GetComponent<Animator>().runtimeAnimatorController, Is.Not.Null);
            Assert.That(File.ReadAllText(SpriteAssetWriter.ToAbsolutePath(result.ReportPath)), Does.Contain("Bones: 2"));
        }

        [Test]
        public void Convert_SyntheticExport_BakesEasedRotationTranslationAndAttachmentSwitch()
        {
            SpineConversionResult result = Convert();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(result.ClipPaths.Single());

            AnimationCurve rotation = Curve(clip, "root/arm", typeof(Transform), "localEulerAnglesRaw.z");
            Assert.That(rotation.Evaluate(0f), Is.EqualTo(90f).Within(1e-3f));
            Assert.That(rotation.Evaluate(0.5f), Is.EqualTo(135f).Within(0.5f));
            Assert.That(rotation.Evaluate(1f), Is.EqualTo(180f).Within(1e-3f));

            AnimationCurve translation = Curve(clip, "root", typeof(Transform), "m_LocalPosition.x");
            Assert.That(translation.Evaluate(1f), Is.EqualTo(0.1f).Within(1e-5f));

            AnimationCurve visible = Curve(clip, "root/body : quad", typeof(GameObject), "m_IsActive");
            Assert.That(visible.Evaluate(0.25f), Is.EqualTo(1f));
            Assert.That(visible.Evaluate(0.75f), Is.EqualTo(0f));
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.True);
        }

        [Test]
        public void Extract_RotatedRegion_RestoresOriginalOrientation()
        {
            string atlasDirectory = Path.Combine(sourceDirectory, "rotated");
            Directory.CreateDirectory(atlasDirectory);
            // 3×2 image packed counter-clockwise into a 2×3 footprint: image bottom-left (red) lands at footprint bottom-right.
            var page = new Texture2D(2, 3, TextureFormat.RGBA32, false);
            page.SetPixels32(new Color32[2 * 3]);
            page.SetPixel(1, 0, Color.red);
            page.Apply();
            File.WriteAllBytes(Path.Combine(atlasDirectory, "rot.png"), page.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(page);
            SpineAtlas atlas = SpineAtlas.Parse("\nrot.png\nsize: 2,3\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\n"
                + "r\n  rotate: true\n  xy: 0, 0\n  size: 3, 2\n  orig: 3, 2\n  offset: 0, 0\n  index: -1\n");

            using (var extractor = new RegionExtractor(atlasDirectory))
            {
                Color32[] pixels = extractor.Extract(atlas.FindRegion("r"), out int width, out int height);

                Assert.That(width, Is.EqualTo(3));
                Assert.That(height, Is.EqualTo(2));
                Assert.That(pixels[0].r, Is.EqualTo(255));
                Assert.That(pixels.Count(pixel => pixel.r == 255), Is.EqualTo(1));
            }
        }

        private SpineConversionResult Convert()
        {
            return SpineConverter.Convert(new SpineConversionRequest
            {
                SkeletonJsonPath = Path.Combine(sourceDirectory, "fixture.json"),
                AtlasPath = Path.Combine(sourceDirectory, "fixture.atlas.txt"),
                OutputFolder = OutputFolder,
                Name = "Fixture"
            });
        }

        private static AnimationCurve Curve(AnimationClip clip, string path, Type type, string property)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, $"Missing curve {path} {property}.");
            return curve;
        }

        private static void WritePage(string path)
        {
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[32 * 16];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                    pixels[y * 32 + x] = new Color32((byte)(x * 16), (byte)(y * 16), 128, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
