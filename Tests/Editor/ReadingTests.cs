using System.Collections.Generic;
using NUnit.Framework;
using SpineTo2DAnimation.Editor.Reading;

namespace SpineTo2DAnimation.Editor.Tests
{
    public sealed class ReadingTests
    {
        [Test]
        public void MiniJson_ParsesNestedValuesAndByteOrderMark()
        {
            var root = (Dictionary<string, object>)MiniJson.Parse("﻿{\"a\":[1,-2.5e1,true,null],\"b\":{\"c\":\"\\u00e9\\n\"}}");

            var array = (List<object>)root["a"];
            Assert.That(array[0], Is.EqualTo(1d));
            Assert.That(array[1], Is.EqualTo(-25d));
            Assert.That(array[2], Is.EqualTo(true));
            Assert.That(array[3], Is.Null);
            Assert.That(((Dictionary<string, object>)root["b"])["c"], Is.EqualTo("é\n"));
        }

        [Test]
        public void MiniJson_RejectsTrailingContent()
        {
            Assert.Throws<System.FormatException>(() => MiniJson.Parse("{} x"));
        }

        [Test]
        public void SpineAtlas_ParsesPagesAndRegions()
        {
            const string text = "\npage.png\nsize: 64,32\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\n"
                + "mouth\n  rotate: true\n  xy: 10, 2\n  size: 8, 4\n  orig: 10, 6\n  offset: 1, 2\n  index: -1\n"
                + "eye\n  rotate: false\n  xy: 0, 0\n  size: 4, 4\n  orig: 4, 4\n  offset: 0, 0\n  index: -1\n";

            SpineAtlas atlas = SpineAtlas.Parse(text);

            Assert.That(atlas.Pages.Count, Is.EqualTo(1));
            Assert.That(atlas.Pages[0].Width, Is.EqualTo(64));
            SpineAtlasRegion mouth = atlas.FindRegion("mouth");
            Assert.That(mouth.Rotate, Is.True);
            Assert.That(mouth.X, Is.EqualTo(10));
            Assert.That(mouth.Width, Is.EqualTo(8));
            Assert.That(mouth.OriginalHeight, Is.EqualTo(6));
            Assert.That(mouth.OffsetY, Is.EqualTo(2));
            Assert.That(mouth.IsWhitespaceStripped, Is.True);
            Assert.That(atlas.FindRegion("eye").Rotate, Is.False);
        }

        [Test]
        public void SpineSkeleton_ReadsSpine38SkinArrayAndCurves()
        {
            SpineSkeletonData data = SpineSkeletonData.Parse(TestFixtures.SkeletonJson);

            Assert.That(data.SpineVersion, Is.EqualTo("3.8.99"));
            Assert.That(data.Bones.Count, Is.EqualTo(2));
            Assert.That(data.Bones[1].Parent, Is.SameAs(data.Bones[0]));
            SpineAttachmentData mesh = data.DefaultSkin.Find("skin", "quad");
            Assert.That(mesh.Type, Is.EqualTo(SpineAttachmentType.Mesh));
            Assert.That(mesh.IsWeighted, Is.True);

            SpineAnimationData idle = data.Animations[0];
            Assert.That(idle.Duration, Is.EqualTo(1f));
            SpineKeyframe first = idle.BoneTimelines.Find(timeline => timeline.Property == "rotate").Keys[0];
            Assert.That(first.Curve.IsBezier, Is.True);
            Assert.That(first.Curve.C1, Is.EqualTo(0.25f));
            Assert.That(first.Curve.C2, Is.EqualTo(0f));
            Assert.That(first.Curve.C3, Is.EqualTo(0.75f));
            Assert.That(first.Curve.C4, Is.EqualTo(1f));
        }

        [Test]
        public void SpineSkeleton_ReadsLegacySkinObjectAndReportsConstraints()
        {
            const string json = "{\"skeleton\":{\"spine\":\"3.7.94\"},\"bones\":[{\"name\":\"root\"}],"
                + "\"slots\":[{\"name\":\"s\",\"bone\":\"root\",\"attachment\":\"a\"}],"
                + "\"ik\":[{\"name\":\"leg\",\"bones\":[\"root\"],\"target\":\"root\",\"order\":2}],"
                + "\"transform\":[{\"name\":\"tc\"}],"
                + "\"skins\":{\"default\":{\"s\":{\"a\":{\"width\":4,\"height\":4}}}}}";

            SpineSkeletonData data = SpineSkeletonData.Parse(json);

            Assert.That(data.DefaultSkin.Find("s", "a").Type, Is.EqualTo(SpineAttachmentType.Region));
            Assert.That(data.IkConstraints.Count, Is.EqualTo(1));
            Assert.That(data.IkConstraints[0].Order, Is.EqualTo(2));
            Assert.That(data.UnsupportedFeatures, Has.Some.Contains("transform"));
        }
    }
}
