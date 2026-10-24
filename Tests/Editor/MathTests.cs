using System.Collections.Generic;
using NUnit.Framework;
using SpineTo2DAnimation.Editor.Conversion;
using SpineTo2DAnimation.Editor.Reading;
using UnityEngine;

namespace SpineTo2DAnimation.Editor.Tests
{
    public sealed class MathTests
    {
        [Test]
        public void EvaluateBezier_WithSymmetricEase_IsHalfAtMiddleAndClampedAtEnds()
        {
            Assert.That(SpineTimelineSampler.EvaluateBezier(0.25f, 0f, 0.75f, 1f, 0.5f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(SpineTimelineSampler.EvaluateBezier(0.25f, 0f, 0.75f, 1f, 0.1f), Is.LessThan(0.1f));
            Assert.That(SpineTimelineSampler.EvaluateBezier(0.25f, 0f, 0.75f, 1f, 0f), Is.EqualTo(0f));
            Assert.That(SpineTimelineSampler.EvaluateBezier(0.25f, 0f, 0.75f, 1f, 1f), Is.EqualTo(1f));
        }

        [Test]
        public void EvaluateBezier_WithLinearControlPoints_IsIdentity()
        {
            float value = SpineTimelineSampler.EvaluateBezier(1f / 3f, 1f / 3f, 2f / 3f, 2f / 3f, 0.3f);
            Assert.That(value, Is.EqualTo(0.3f).Within(1e-4f));
        }

        [Test]
        public void SampleRotate_TakesShortestPathAcrossZero()
        {
            var timeline = new SpineTimeline
            {
                Property = "rotate",
                Keys = new List<SpineKeyframe>
                {
                    new SpineKeyframe { Time = 0f, Value1 = 350f },
                    new SpineKeyframe { Time = 1f, Value1 = 10f }
                }
            };

            Assert.That(SpineTimelineSampler.SampleRotate(timeline, 0.5f), Is.EqualTo(360f).Within(1e-3f));
            Assert.That(SpineTimelineSampler.SampleRotate(timeline, 2f), Is.EqualTo(10f));
        }

        [Test]
        public void SampleAttachment_IsSteppedAndUsesSetupBeforeFirstKey()
        {
            var timeline = new SpineTimeline
            {
                Property = "attachment",
                Keys = new List<SpineKeyframe> { new SpineKeyframe { Time = 0.5f, Name = "closed" } }
            };

            Assert.That(SpineTimelineSampler.SampleAttachment(timeline, 0.25f, "open"), Is.EqualTo("open"));
            Assert.That(SpineTimelineSampler.SampleAttachment(timeline, 0.75f, "open"), Is.EqualTo("closed"));
        }

        [Test]
        public void SimilarityFit_RecoversKnownTransform()
        {
            var expected = new Similarity2D { Scale = 0.5f, RotationDegrees = 30f, Translation = new Vector2(12f, -4f) };
            var source = new List<Vector2> { new Vector2(0f, 0f), new Vector2(40f, 0f), new Vector2(40f, 20f), new Vector2(0f, 20f) };
            var target = source.ConvertAll(expected.Apply);

            Similarity2D fit = Similarity2D.Fit(source, target);

            Assert.That(fit.Scale, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(fit.RotationDegrees, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(fit.Translation.x, Is.EqualTo(12f).Within(1e-3f));
            Assert.That(fit.MaxResidual, Is.LessThan(1e-3f));
            Assert.That((fit.ApplyInverse(target[2]) - source[2]).magnitude, Is.LessThan(1e-3f));
        }

        [Test]
        public void Simplify_DropsRedundantKeysWithinTolerance()
        {
            var ramp = new AnimationCurve();
            var wave = new AnimationCurve();
            for (int frame = 0; frame <= 30; frame++)
            {
                float time = frame / 30f;
                ramp.AddKey(time, 2f * time);
                wave.AddKey(time, Mathf.Sin(time * Mathf.PI * 2f) * 10f);
            }

            AnimationCurve simplifiedRamp = SpineAnimationBaker.Simplify(ramp, 0.001f);
            AnimationCurve simplifiedWave = SpineAnimationBaker.Simplify(wave, 0.05f);

            Assert.That(simplifiedRamp.length, Is.EqualTo(2));
            Assert.That(simplifiedWave.length, Is.LessThan(wave.length));
            for (int frame = 0; frame <= 30; frame++)
            {
                float time = frame / 30f;
                float linear = LinearEvaluate(simplifiedWave, time);
                Assert.That(Mathf.Abs(linear - wave.keys[frame].value), Is.LessThanOrEqualTo(0.05f + 1e-4f));
            }
        }

        private static float LinearEvaluate(AnimationCurve curve, float time)
        {
            Keyframe[] keys = curve.keys;
            for (int index = 0; index < keys.Length - 1; index++)
            {
                if (time <= keys[index + 1].time)
                {
                    float t = (time - keys[index].time) / (keys[index + 1].time - keys[index].time);
                    return keys[index].value + (keys[index + 1].value - keys[index].value) * t;
                }
            }

            return keys[keys.Length - 1].value;
        }

        [Test]
        public void Matrix2D_ComposesParentRotation()
        {
            Matrix2D arm = Matrix2D.Identity * Matrix2D.FromTransform(10f, 0f, 90f, 1f, 1f);

            Vector2 world = arm.Apply(new Vector2(-8f, 2f));

            Assert.That(world.x, Is.EqualTo(8f).Within(1e-4f));
            Assert.That(world.y, Is.EqualTo(-8f).Within(1e-4f));
            Assert.That(arm.RotationDegrees, Is.EqualTo(90f).Within(1e-3f));
        }
    }
}
