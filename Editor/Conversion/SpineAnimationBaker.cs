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
            List<SlotBinding> slots,
            Transform root,
            Transform[] bones,
            float pixelsPerUnit,
            float frameRate)
        {
            var clip = new AnimationClip { name = animation.Name, frameRate = frameRate };
            float[] times = SampleTimes(animation, frameRate);

            foreach (IGrouping<string, SpineTimeline> boneTimelines in animation.BoneTimelines.GroupBy(timeline => timeline.Target))
            {
                SpineBoneData bone = skeleton.FindBone(boneTimelines.Key);
                if (bone == null)
                    continue;

                string path = SpineConverter.RelativePath(root, bones[bone.Index]);
                foreach (SpineTimeline timeline in boneTimelines)
                    BakeBoneTimeline(clip, timeline, bone, path, times, pixelsPerUnit);
            }

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

        private static void SetLinear(AnimationClip clip, string path, System.Type type, string property, AnimationCurve curve)
        {
            for (int index = 0; index < curve.length; index++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Linear);
            }

            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
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
