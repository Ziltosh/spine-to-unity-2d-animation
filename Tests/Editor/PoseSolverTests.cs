using NUnit.Framework;
using SpineTo2DAnimation.Editor.Conversion;
using SpineTo2DAnimation.Editor.Reading;
using UnityEngine;

namespace SpineTo2DAnimation.Editor.Tests
{
    public sealed class PoseSolverTests
    {
        // Two 10 px bones along +x from the origin; the target bone sits at (10, 10).
        private const string LegJson = "{"
            + "\"skeleton\":{\"spine\":\"3.8.99\"},"
            + "\"bones\":["
            + "{\"name\":\"root\"},"
            + "{\"name\":\"upper\",\"parent\":\"root\",\"length\":10},"
            + "{\"name\":\"lower\",\"parent\":\"upper\",\"x\":10,\"length\":10},"
            + "{\"name\":\"target\",\"parent\":\"root\",\"x\":10,\"y\":10}"
            + "],"
            + "\"ik\":[{\"name\":\"leg\",\"bones\":[\"upper\",\"lower\"],\"target\":\"target\"BEND}],"
            + "\"animations\":{\"walk\":{\"bones\":{\"target\":{\"translate\":[{\"time\":0},{\"time\":1,\"x\":-10,\"y\":0}]}}}}"
            + "}";

        [TestCase(true)]
        [TestCase(false)]
        public void TwoBoneIk_PlacesChainTipOnTarget(bool bendPositive)
        {
            SpineSkeletonData data = SpineSkeletonData.Parse(LegJson.Replace("BEND", bendPositive ? string.Empty : ",\"bendPositive\":false"));
            var solver = new SpinePoseSolver(data);

            solver.SolvedPose(null, 0f, out Matrix2D[] world);

            Vector2 tip = world[2].Apply(new Vector2(10f, 0f));
            Assert.That(tip.x, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(tip.y, Is.EqualTo(10f).Within(1e-3f));
            Vector2 knee = world[2].Apply(Vector2.zero);
            // Bend positive keeps the knee below-right of the chord (0,0)→(10,10); negative mirrors it.
            Assert.That(knee.x > knee.y, Is.EqualTo(bendPositive));
        }

        [Test]
        public void TwoBoneIk_FollowsAnimatedTarget()
        {
            SpineSkeletonData data = SpineSkeletonData.Parse(LegJson.Replace("BEND", string.Empty));
            var solver = new SpinePoseSolver(data);

            solver.SolvedPose(data.Animations[0], 1f, out Matrix2D[] world);

            Vector2 tip = world[2].Apply(new Vector2(10f, 0f));
            Assert.That(tip.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(tip.y, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(solver.ConstrainedBones, Is.EquivalentTo(new[] { 1, 2 }));
        }

        [Test]
        public void TwoBoneIk_UnreachableTarget_StretchesChainTowardIt()
        {
            SpineSkeletonData data = SpineSkeletonData.Parse(LegJson.Replace("BEND", string.Empty).Replace("\"x\":10,\"y\":10", "\"x\":40,\"y\":0"));
            var solver = new SpinePoseSolver(data);

            solver.SolvedPose(null, 0f, out Matrix2D[] world);

            Vector2 tip = world[2].Apply(new Vector2(10f, 0f));
            Assert.That(tip.x, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(tip.y, Is.EqualTo(0f).Within(1e-3f));
        }
    }
}
