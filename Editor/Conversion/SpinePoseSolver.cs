using System.Collections.Generic;
using System.Linq;
using SpineTo2DAnimation.Editor.Reading;
using UnityEngine;

namespace SpineTo2DAnimation.Editor.Conversion
{
    /// <summary>Local bone transform, in skeleton pixels and degrees.</summary>
    public struct SpineLocalPose
    {
        public float X;
        public float Y;
        public float Rotation;
        public float ScaleX;
        public float ScaleY;

        public Matrix2D ToMatrix() => Matrix2D.FromTransform(X, Y, Rotation, ScaleX, ScaleY);
    }

    /// <summary>
    /// Evaluates a Spine pose (setup or animated) and applies IK constraints the way the Spine 3.8 runtime
    /// does for one- and two-bone chains, so that IK can be baked into plain bone rotations.
    /// </summary>
    public sealed class SpinePoseSolver
    {
        private readonly SpineSkeletonData skeleton;
        private readonly Dictionary<int, List<int>> descendants = new Dictionary<int, List<int>>();

        public SpinePoseSolver(SpineSkeletonData skeleton)
        {
            this.skeleton = skeleton;
            foreach (SpineBoneData bone in skeleton.Bones)
                descendants[bone.Index] = new List<int>();

            foreach (SpineBoneData bone in skeleton.Bones)
            {
                for (SpineBoneData ancestor = bone.Parent; ancestor != null; ancestor = ancestor.Parent)
                    descendants[ancestor.Index].Add(bone.Index);
            }
        }

        /// <summary>Bones whose rotation is driven by at least one IK constraint.</summary>
        public HashSet<int> ConstrainedBones =>
            new HashSet<int>(skeleton.IkConstraints.SelectMany(constraint => constraint.Bones.Select(bone => bone.Index)));

        public bool HasConstraints => skeleton.IkConstraints.Count > 0;

        public SpineLocalPose[] SetupPose()
        {
            var pose = new SpineLocalPose[skeleton.Bones.Count];
            foreach (SpineBoneData bone in skeleton.Bones)
            {
                pose[bone.Index] = new SpineLocalPose
                {
                    X = bone.X,
                    Y = bone.Y,
                    Rotation = bone.Rotation,
                    ScaleX = bone.ScaleX,
                    ScaleY = bone.ScaleY
                };
            }

            return pose;
        }

        /// <summary>Setup pose with the bone timelines of <paramref name="animation"/> applied at <paramref name="time"/>.</summary>
        public SpineLocalPose[] AnimatedPose(SpineAnimationData animation, float time)
        {
            SpineLocalPose[] pose = SetupPose();
            foreach (SpineTimeline timeline in animation.BoneTimelines)
            {
                SpineBoneData bone = skeleton.FindBone(timeline.Target);
                if (bone == null)
                    continue;

                switch (timeline.Property)
                {
                    case "rotate":
                        pose[bone.Index].Rotation = bone.Rotation + SpineTimelineSampler.SampleRotate(timeline, time);
                        break;
                    case "translate":
                        Vector2 offset = SpineTimelineSampler.SampleVector(timeline, time, 0f);
                        pose[bone.Index].X = bone.X + offset.x;
                        pose[bone.Index].Y = bone.Y + offset.y;
                        break;
                    case "scale":
                        Vector2 factor = SpineTimelineSampler.SampleVector(timeline, time, 1f);
                        pose[bone.Index].ScaleX = bone.ScaleX * factor.x;
                        pose[bone.Index].ScaleY = bone.ScaleY * factor.y;
                        break;
                }
            }

            return pose;
        }

        public Matrix2D[] WorldTransforms(SpineLocalPose[] pose)
        {
            var world = new Matrix2D[skeleton.Bones.Count];
            foreach (SpineBoneData bone in skeleton.Bones)
                UpdateWorld(pose, world, bone);

            return world;
        }

        /// <summary>Applies every IK constraint in order, updating local rotations and world transforms in place.</summary>
        public void ApplyIk(SpineLocalPose[] pose, Matrix2D[] world)
        {
            foreach (SpineIkConstraintData constraint in skeleton.IkConstraints)
            {
                if (constraint.Mix <= 0f)
                    continue;

                Matrix2D target = world[constraint.Target.Index];
                if (constraint.Bones.Count == 1)
                    ApplyOneBone(pose, world, constraint.Bones[0], target.X, target.Y, constraint.Mix);
                else
                    ApplyTwoBones(pose, world, constraint.Bones[0], constraint.Bones[1], target.X, target.Y, constraint.BendPositive ? 1 : -1, constraint.Mix);
            }
        }

        /// <summary>Convenience: pose with IK solved (setup pose when <paramref name="animation"/> is null).</summary>
        public SpineLocalPose[] SolvedPose(SpineAnimationData animation, float time, out Matrix2D[] world)
        {
            SpineLocalPose[] pose = animation == null ? SetupPose() : AnimatedPose(animation, time);
            world = WorldTransforms(pose);
            ApplyIk(pose, world);
            return pose;
        }

        private void ApplyOneBone(SpineLocalPose[] pose, Matrix2D[] world, SpineBoneData bone, float targetX, float targetY, float alpha)
        {
            Vector2 local = ParentLocal(world, bone, targetX, targetY);
            float rotation = Mathf.Atan2(local.y - pose[bone.Index].Y, local.x - pose[bone.Index].X) * Mathf.Rad2Deg;
            pose[bone.Index].Rotation += alpha * Mathf.DeltaAngle(pose[bone.Index].Rotation, rotation);
            Refresh(pose, world, bone);
        }

        /// <summary>
        /// Law-of-cosines solution of Spine's two-bone IK (uniform scale): the parent turns so the child bone
        /// tip reaches the target, bending toward <paramref name="bendDirection"/>.
        /// </summary>
        private void ApplyTwoBones(
            SpineLocalPose[] pose,
            Matrix2D[] world,
            SpineBoneData parent,
            SpineBoneData child,
            float targetX,
            float targetY,
            int bendDirection,
            float alpha)
        {
            SpineLocalPose parentPose = pose[parent.Index];
            SpineLocalPose childPose = pose[child.Index];

            // Target and child position expressed in the frame of the parent's parent, relative to the parent origin.
            Vector2 target = ParentLocal(world, parent, targetX, targetY) - new Vector2(parentPose.X, parentPose.Y);
            Matrix2D parentWorld = world[parent.Index];
            Vector2 childWorld = parentWorld.Apply(new Vector2(childPose.X, childPose.Y));
            Vector2 childOffset = ParentLocal(world, parent, childWorld.x, childWorld.y) - new Vector2(parentPose.X, parentPose.Y);

            float l1 = childOffset.magnitude;
            float l2 = child.Length * childPose.ScaleX * parentPose.ScaleX;
            if (l1 < 1e-4f || l2 < 1e-4f)
            {
                ApplyOneBone(pose, world, parent, targetX, targetY, alpha);
                return;
            }

            float cos = (target.sqrMagnitude - l1 * l1 - l2 * l2) / (2f * l1 * l2);
            cos = Mathf.Clamp(cos, -1f, 1f);
            float a2 = Mathf.Acos(cos) * bendDirection;
            float a = l1 + l2 * cos;
            float b = l2 * Mathf.Sin(a2);
            float a1 = Mathf.Atan2(target.y * a - target.x * b, target.x * a + target.y * b);

            float offsetAngle = Mathf.Atan2(childPose.Y, childPose.X);
            float parentRotation = (a1 - offsetAngle) * Mathf.Rad2Deg;
            float childRotation = (a2 + offsetAngle) * Mathf.Rad2Deg;

            pose[parent.Index].Rotation += alpha * Mathf.DeltaAngle(parentPose.Rotation, parentRotation);
            pose[child.Index].Rotation += alpha * Mathf.DeltaAngle(childPose.Rotation, childRotation);
            Refresh(pose, world, parent);
        }

        /// <summary>World point expressed in the local frame of <paramref name="bone"/>'s parent.</summary>
        private static Vector2 ParentLocal(Matrix2D[] world, SpineBoneData bone, float x, float y)
        {
            if (bone.Parent == null)
                return new Vector2(x, y);

            Matrix2D parent = world[bone.Parent.Index];
            float determinant = parent.A * parent.D - parent.B * parent.C;
            float dx = x - parent.X;
            float dy = y - parent.Y;
            return new Vector2((dx * parent.D - dy * parent.B) / determinant, (dy * parent.A - dx * parent.C) / determinant);
        }

        private void Refresh(SpineLocalPose[] pose, Matrix2D[] world, SpineBoneData bone)
        {
            UpdateWorld(pose, world, bone);
            foreach (int index in descendants[bone.Index])
                UpdateWorld(pose, world, skeleton.Bones[index]);
        }

        private static void UpdateWorld(SpineLocalPose[] pose, Matrix2D[] world, SpineBoneData bone)
        {
            Matrix2D local = pose[bone.Index].ToMatrix();
            world[bone.Index] = bone.Parent == null ? local : world[bone.Parent.Index] * local;
        }
    }
}
