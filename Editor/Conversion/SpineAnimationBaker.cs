using System.Collections.Generic;
using System.Linq;
using SpineTo2DAnimation.Editor.Reading;
using UnityEditor;
using UnityEngine;

namespace SpineTo2DAnimation.Editor.Conversion
{
    /// <summary>
    /// Bakes a Spine animation into a Unity AnimationClip by sampling every timeline at a fixed rate
    /// (plus each Spine key time), which reproduces Spine's Bezier easing with linear segments.
    /// </summary>
    public static class SpineAnimationBaker
    {
        public sealed class AttachmentBinding
        {
            public string Key;
            public string Path;
            public SpineColor AttachmentColor;
        }

        public sealed class SlotBinding
        {
            public SpineSlotData Slot;
            public List<AttachmentBinding> Attachments = new List<AttachmentBinding>();
        }

        public static AnimationClip Bake(
            SpineAnimationData animation,
            SpineSkeletonData skeleton,
            SpinePoseSolver solver,
            List<SlotBinding> slots,
            Transform root,
            Transform[] bones,
            float pixelsPerUnit,
            float frameRate)
        {
            var clip = new AnimationClip { name = animation.Name, frameRate = frameRate };
            float[] times = SampleTimes(animation, frameRate);
            HashSet<int> constrained = solver.ConstrainedBones;

            foreach (IGrouping<string, SpineTimeline> boneTimelines in animation.BoneTimelines.GroupBy(timeline => timeline.Target))
            {
                SpineBoneData bone = skeleton.FindBone(boneTimelines.Key);
                if (bone == null)
                    continue;

                string path = SpineConverter.RelativePath(root, bones[bone.Index]);
                foreach (SpineTimeline timeline in boneTimelines)
                {
                    // The rotation of an IK-driven bone comes from the solved pose below.
                    if (timeline.Property == "rotate" && constrained.Contains(bone.Index))
                        continue;

                    BakeBoneTimeline(clip, timeline, bone, path, times, pixelsPerUnit);
                }
            }

            if (constrained.Count > 0)
                BakeConstrainedRotations(clip, animation, solver, constrained, root, bones, times);

            foreach (SpineTimeline timeline in animation.SlotTimelines)
            {
                SlotBinding slot = slots.Find(binding => binding.Slot.Name == timeline.Target);
                if (slot == null || slot.Attachments.Count == 0)
                    continue;

                if (timeline.Property == "attachment")
                    BakeAttachmentTimeline(clip, timeline, slot);
                else
                    BakeColorTimeline(clip, timeline, slot, times);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        internal static float[] SampleTimes(SpineAnimationData animation, float frameRate)
        {
            float duration = animation.Duration;
            var times = new SortedSet<float>();
            int frames = Mathf.CeilToInt(duration * frameRate);
            for (int frame = 0; frame <= frames; frame++)
                times.Add(Mathf.Min(duration, frame / frameRate));

            foreach (SpineTimeline timeline in animation.BoneTimelines.Concat(animation.SlotTimelines))
            {
                foreach (SpineKeyframe key in timeline.Keys)
                    times.Add(key.Time);
            }

            return times.ToArray();
        }

        private static void BakeBoneTimeline(AnimationClip clip, SpineTimeline timeline, SpineBoneData bone, string path, float[] times, float ppu)
        {
            switch (timeline.Property)
            {
                case "rotate":
                {
                    var curve = new AnimationCurve();
                    float previous = float.NaN;
                    foreach (float time in times)
                    {
                        float angle = bone.Rotation + SpineTimelineSampler.SampleRotate(timeline, time);
                        // Keep the curve continuous: Unity interpolates raw Euler angles linearly.
                        if (!float.IsNaN(previous))
                            angle = previous + Mathf.DeltaAngle(previous, angle);
                        curve.AddKey(time, angle);
                        previous = angle;
                    }

                    SetLinear(clip, path, typeof(Transform), "localEulerAnglesRaw.z", curve);
                    SetLinear(clip, path, typeof(Transform), "localEulerAnglesRaw.x", Constant(times, 0f));
                    SetLinear(clip, path, typeof(Transform), "localEulerAnglesRaw.y", Constant(times, 0f));
                    break;
                }
                case "translate":
                {
                    var x = new AnimationCurve();
                    var y = new AnimationCurve();
                    foreach (float time in times)
                    {
                        Vector2 offset = SpineTimelineSampler.SampleVector(timeline, time, 0f);
                        x.AddKey(time, (bone.X + offset.x) / ppu);
                        y.AddKey(time, (bone.Y + offset.y) / ppu);
                    }

                    SetLinear(clip, path, typeof(Transform), "m_LocalPosition.x", x);
                    SetLinear(clip, path, typeof(Transform), "m_LocalPosition.y", y);
                    break;
                }
                case "scale":
                {
                    var x = new AnimationCurve();
                    var y = new AnimationCurve();
                    foreach (float time in times)
                    {
                        Vector2 factor = SpineTimelineSampler.SampleVector(timeline, time, 1f);
                        x.AddKey(time, bone.ScaleX * factor.x);
                        y.AddKey(time, bone.ScaleY * factor.y);
                    }

                    SetLinear(clip, path, typeof(Transform), "m_LocalScale.x", x);
                    SetLinear(clip, path, typeof(Transform), "m_LocalScale.y", y);
                    break;
                }
            }
        }

        /// <summary>Bakes IK into forward kinematics: the solved local rotation of each constrained bone at every sample.</summary>
        private static void BakeConstrainedRotations(
            AnimationClip clip,
            SpineAnimationData animation,
            SpinePoseSolver solver,
            HashSet<int> constrained,
            Transform root,
            Transform[] bones,
            float[] times)
        {
            var curves = constrained.ToDictionary(index => index, index => new AnimationCurve());
            var previous = constrained.ToDictionary(index => index, index => float.NaN);

            foreach (float time in times)
            {
                SpineLocalPose[] pose = solver.SolvedPose(animation, time, out _);
                foreach (int index in constrained)
                {
                    float angle = pose[index].Rotation;
                    if (!float.IsNaN(previous[index]))
                        angle = previous[index] + Mathf.DeltaAngle(previous[index], angle);
                    curves[index].AddKey(time, angle);
                    previous[index] = angle;
                }
            }

            foreach (KeyValuePair<int, AnimationCurve> pair in curves)
            {
                string path = SpineConverter.RelativePath(root, bones[pair.Key]);
                SetLinear(clip, path, typeof(Transform), "localEulerAnglesRaw.z", pair.Value);
                SetLinear(clip, path, typeof(Transform), "localEulerAnglesRaw.x", Constant(times, 0f));
                SetLinear(clip, path, typeof(Transform), "localEulerAnglesRaw.y", Constant(times, 0f));
            }
        }

        private static void BakeAttachmentTimeline(AnimationClip clip, SpineTimeline timeline, SlotBinding slot)
        {
            foreach (AttachmentBinding attachment in slot.Attachments)
            {
                var curve = new AnimationCurve();
                curve.AddKey(0f, attachment.Key == slot.Slot.Attachment ? 1f : 0f);
                foreach (SpineKeyframe key in timeline.Keys)
                {
                    float value = attachment.Key == key.Name ? 1f : 0f;
                    int existing = FindKey(curve, key.Time);
                    if (existing >= 0)
                        curve.MoveKey(existing, new Keyframe(key.Time, value));
                    else
                        curve.AddKey(key.Time, value);
                }

                for (int index = 0; index < curve.length; index++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Constant);
                }

                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(attachment.Path, typeof(GameObject), "m_IsActive"), curve);
            }
        }

        private static void BakeColorTimeline(AnimationClip clip, SpineTimeline timeline, SlotBinding slot, float[] times)
        {
            foreach (AttachmentBinding attachment in slot.Attachments)
            {
                var r = new AnimationCurve();
                var g = new AnimationCurve();
                var b = new AnimationCurve();
                var a = new AnimationCurve();
                foreach (float time in times)
                {
                    SpineColor color = SpineTimelineSampler.SampleColor(timeline, time, slot.Slot.Color).Multiply(attachment.AttachmentColor);
                    r.AddKey(time, color.R);
                    g.AddKey(time, color.G);
                    b.AddKey(time, color.B);
                    a.AddKey(time, color.A);
                }

                SetLinear(clip, attachment.Path, typeof(SpriteRenderer), "m_Color.r", r);
                SetLinear(clip, attachment.Path, typeof(SpriteRenderer), "m_Color.g", g);
                SetLinear(clip, attachment.Path, typeof(SpriteRenderer), "m_Color.b", b);
                SetLinear(clip, attachment.Path, typeof(SpriteRenderer), "m_Color.a", a);
            }
        }

        private static AnimationCurve Constant(float[] times, float value)
        {
            var curve = new AnimationCurve();
            curve.AddKey(times[0], value);
            if (times.Length > 1)
                curve.AddKey(times[times.Length - 1], value);
            return curve;
        }

        private static void SetLinear(AnimationClip clip, string path, System.Type type, string property, AnimationCurve sampled)
        {
            AnimationCurve curve = Simplify(sampled, ToleranceFor(property));
            for (int index = 0; index < curve.length; index++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            }

            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        /// <summary>Largest error accepted when dropping a sampled key: 0.05° in rotation, 0.05 px at 100 px/unit, 0.2 % in color.</summary>
        private static float ToleranceFor(string property)
        {
            if (property.StartsWith("localEulerAngles", System.StringComparison.Ordinal))
                return 0.05f;
            if (property.StartsWith("m_Color", System.StringComparison.Ordinal))
                return 0.002f;
            return 0.0005f;
        }

        /// <summary>
        /// Keeps only the sampled keys needed for linear interpolation to stay within <paramref name="tolerance"/>
        /// of every sample (Douglas–Peucker on the time series); constant curves collapse to their two ends.
        /// </summary>
        public static AnimationCurve Simplify(AnimationCurve sampled, float tolerance)
        {
            Keyframe[] keys = sampled.keys;
            if (keys.Length <= 2)
                return sampled;

            var keep = new bool[keys.Length];
            keep[0] = true;
            keep[keys.Length - 1] = true;
            var ranges = new Stack<(int Start, int End)>();
            ranges.Push((0, keys.Length - 1));

            while (ranges.Count > 0)
            {
                (int start, int end) = ranges.Pop();
                float worst = tolerance;
                int worstIndex = -1;
                for (int index = start + 1; index < end; index++)
                {
                    float span = keys[end].time - keys[start].time;
                    float t = span <= 0f ? 0f : (keys[index].time - keys[start].time) / span;
                    float interpolated = keys[start].value + (keys[end].value - keys[start].value) * t;
                    float error = Mathf.Abs(interpolated - keys[index].value);
                    if (error > worst)
                    {
                        worst = error;
                        worstIndex = index;
                    }
                }

                if (worstIndex < 0)
                    continue;

                keep[worstIndex] = true;
                ranges.Push((start, worstIndex));
                ranges.Push((worstIndex, end));
            }

            var simplified = new AnimationCurve();
            for (int index = 0; index < keys.Length; index++)
            {
                if (keep[index])
                    simplified.AddKey(keys[index].time, keys[index].value);
            }

            return simplified;
        }

        private static int FindKey(AnimationCurve curve, float time)
        {
            for (int index = 0; index < curve.length; index++)
            {
                if (Mathf.Abs(curve.keys[index].time - time) < 1e-5f)
                    return index;
            }

            return -1;
        }
    }
}
