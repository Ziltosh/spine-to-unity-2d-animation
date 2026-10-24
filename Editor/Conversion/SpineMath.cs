using System;
using System.Collections.Generic;
using SpineTo2DAnimation.Editor.Reading;
using UnityEngine;

namespace SpineTo2DAnimation.Editor.Conversion
{
    /// <summary>2D affine transform: x' = A·x + B·y + X, y' = C·x + D·y + Y.</summary>
    public struct Matrix2D
    {
        public float A;
        public float B;
        public float C;
        public float D;
        public float X;
        public float Y;

        public static readonly Matrix2D Identity = new Matrix2D { A = 1f, D = 1f };

        /// <summary>Bone local transform without shear, as Spine composes it for the "normal" inheritance mode.</summary>
        public static Matrix2D FromTransform(float x, float y, float rotationDegrees, float scaleX, float scaleY)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Matrix2D { A = cos * scaleX, B = -sin * scaleY, C = sin * scaleX, D = cos * scaleY, X = x, Y = y };
        }

        public Vector2 Apply(Vector2 point) => new Vector2(A * point.x + B * point.y + X, C * point.x + D * point.y + Y);

        public float RotationDegrees => Mathf.Atan2(C, A) * Mathf.Rad2Deg;

        public float ScaleX => Mathf.Sqrt(A * A + C * C);

        public float ScaleY => Mathf.Sqrt(B * B + D * D);

        public static Matrix2D operator *(Matrix2D parent, Matrix2D child)
        {
            return new Matrix2D
            {
                A = parent.A * child.A + parent.B * child.C,
                B = parent.A * child.B + parent.B * child.D,
                C = parent.C * child.A + parent.D * child.C,
                D = parent.C * child.B + parent.D * child.D,
                X = parent.A * child.X + parent.B * child.Y + parent.X,
                Y = parent.C * child.X + parent.D * child.Y + parent.Y
            };
        }
    }

    /// <summary>Similarity transform (uniform scale, rotation, translation) mapping sprite pixels to skeleton pixels.</summary>
    public struct Similarity2D
    {
        public float Scale;
        public float RotationDegrees;
        public Vector2 Translation;
        /// <summary>Largest distance, in skeleton pixels, between a fitted point and its target.</summary>
        public float MaxResidual;

        public Vector2 Apply(Vector2 point)
        {
            float radians = RotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(Scale * (cos * point.x - sin * point.y), Scale * (sin * point.x + cos * point.y)) + Translation;
        }

        public Vector2 ApplyInverse(Vector2 point)
        {
            Vector2 local = point - Translation;
            float radians = -RotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(cos * local.x - sin * local.y, sin * local.x + cos * local.y) / Scale;
        }

        /// <summary>Least-squares fit (Procrustes, reflection excluded) of target ≈ s·R·source + t.</summary>
        public static Similarity2D Fit(IReadOnlyList<Vector2> source, IReadOnlyList<Vector2> target)
        {
            if (source.Count != target.Count || source.Count < 2)
                throw new ArgumentException("Similarity fit needs at least two matching points.");

            Vector2 sourceCentroid = Vector2.zero;
            Vector2 targetCentroid = Vector2.zero;
            for (int index = 0; index < source.Count; index++)
            {
                sourceCentroid += source[index];
                targetCentroid += target[index];
            }

            sourceCentroid /= source.Count;
            targetCentroid /= source.Count;

            double dot = 0d;
            double cross = 0d;
            double sourceNorm = 0d;
            for (int index = 0; index < source.Count; index++)
            {
                Vector2 q = source[index] - sourceCentroid;
                Vector2 p = target[index] - targetCentroid;
                dot += q.x * p.x + q.y * p.y;
                cross += q.x * p.y - q.y * p.x;
                sourceNorm += q.x * q.x + q.y * q.y;
            }

            if (sourceNorm <= double.Epsilon)
                throw new ArgumentException("Similarity fit source points are degenerate.");

            var fit = new Similarity2D
            {
                Scale = (float)(Math.Sqrt(dot * dot + cross * cross) / sourceNorm),
                RotationDegrees = (float)(Math.Atan2(cross, dot) * 180d / Math.PI)
            };
            fit.Translation = targetCentroid - (fit.Apply(sourceCentroid) - fit.Translation);

            float maxResidual = 0f;
            for (int index = 0; index < source.Count; index++)
                maxResidual = Mathf.Max(maxResidual, (fit.Apply(source[index]) - target[index]).magnitude);

            fit.MaxResidual = maxResidual;
            return fit;
        }
    }

    /// <summary>Evaluation of Spine 3.8 timelines at an arbitrary time.</summary>
    public static class SpineTimelineSampler
    {
        /// <summary>Bezier easing from (0,0) to (1,1) with control points (c1,c2) and (c3,c4), solved for x.</summary>
        public static float EvaluateBezier(float c1, float c2, float c3, float c4, float x)
        {
            if (x <= 0f)
                return 0f;
            if (x >= 1f)
                return 1f;

            float low = 0f;
            float high = 1f;
            float t = x;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                t = (low + high) * 0.5f;
                float bx = Cubic(c1, c3, t);
                if (Mathf.Abs(bx - x) < 1e-6f)
                    break;
                if (bx < x)
                    low = t;
                else
                    high = t;
            }

            return Cubic(c2, c4, t);
        }

        public static float Percent(SpineCurve curve, float linearPercent)
        {
            if (curve.Stepped)
                return 0f;

            return curve.IsBezier ? EvaluateBezier(curve.C1, curve.C2, curve.C3, curve.C4, linearPercent) : linearPercent;
        }

        /// <summary>Returns the index of the key active at <paramref name="time"/> and the eased percent toward the next key.</summary>
        public static int Locate(List<SpineKeyframe> keys, float time, out float percent)
        {
            percent = 0f;
            if (keys.Count == 0 || time < keys[0].Time)
                return -1;

            int last = keys.Count - 1;
            if (time >= keys[last].Time)
                return last;

            int index = 0;
            while (index < last && keys[index + 1].Time <= time)
                index++;

            SpineKeyframe key = keys[index];
            float span = keys[index + 1].Time - key.Time;
            float linear = span <= 0f ? 0f : (time - key.Time) / span;
            percent = Percent(key.Curve, linear);
            return index;
        }

        /// <summary>Rotate timeline value (degrees, relative to setup) using Spine's shortest-path interpolation.</summary>
        public static float SampleRotate(SpineTimeline timeline, float time)
        {
            int index = Locate(timeline.Keys, time, out float percent);
            if (index < 0)
                return 0f;

            float previous = timeline.Keys[index].Value1;
            if (index == timeline.Keys.Count - 1)
                return previous;

            float delta = timeline.Keys[index + 1].Value1 - previous;
            delta -= (16384 - (int)(16384.499999999996 - delta / 360f)) * 360f;
            return previous + delta * percent;
        }

        /// <summary>Two-component timeline value (translate offset or scale factor).</summary>
        public static Vector2 SampleVector(SpineTimeline timeline, float time, float defaultValue)
        {
            int index = Locate(timeline.Keys, time, out float percent);
            if (index < 0)
                return new Vector2(defaultValue, defaultValue);

            SpineKeyframe key = timeline.Keys[index];
            if (index == timeline.Keys.Count - 1)
                return new Vector2(key.Value1, key.Value2);

            SpineKeyframe next = timeline.Keys[index + 1];
            return new Vector2(
                key.Value1 + (next.Value1 - key.Value1) * percent,
                key.Value2 + (next.Value2 - key.Value2) * percent);
        }

        public static SpineColor SampleColor(SpineTimeline timeline, float time, SpineColor setup)
        {
            int index = Locate(timeline.Keys, time, out float percent);
            if (index < 0)
                return setup;

            SpineColor color = timeline.Keys[index].Color;
            if (index == timeline.Keys.Count - 1)
                return color;

            SpineColor next = timeline.Keys[index + 1].Color;
            return new SpineColor
            {
                R = color.R + (next.R - color.R) * percent,
                G = color.G + (next.G - color.G) * percent,
                B = color.B + (next.B - color.B) * percent,
                A = color.A + (next.A - color.A) * percent
            };
        }

        /// <summary>Attachment name shown at <paramref name="time"/>; the setup attachment before the first key.</summary>
        public static string SampleAttachment(SpineTimeline timeline, float time, string setup)
        {
            int index = Locate(timeline.Keys, time, out _);
            return index < 0 ? setup : timeline.Keys[index].Name;
        }

        private static float Cubic(float p1, float p2, float t)
        {
            float inverse = 1f - t;
            return 3f * inverse * inverse * t * p1 + 3f * inverse * t * t * p2 + t * t * t;
        }
    }
}
