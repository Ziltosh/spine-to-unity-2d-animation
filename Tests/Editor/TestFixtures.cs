namespace SpineTo2DAnimation.Editor.Tests
{
    /// <summary>
    /// Synthetic Spine 3.8 export used by the tests. Bone "arm" sits at (10, 0) rotated by 90°, so its
    /// local coordinates map to skeleton coordinates as (x, y) → (10 − y, x). The weighted mesh "quad"
    /// is laid out exactly on its 16×16 image translated by (−8, −8): two vertices follow "root",
    /// two follow "arm".
    /// </summary>
    internal static class TestFixtures
    {
        public const string SkeletonJson = "{"
            + "\"skeleton\":{\"spine\":\"3.8.99\"},"
            + "\"bones\":[{\"name\":\"root\"},{\"name\":\"arm\",\"parent\":\"root\",\"x\":10,\"rotation\":90,\"length\":12}],"
            + "\"slots\":[{\"name\":\"body\",\"bone\":\"root\",\"attachment\":\"quad\"},{\"name\":\"skin\",\"bone\":\"root\",\"attachment\":\"quad\"}],"
            + "\"skins\":[{\"name\":\"default\",\"attachments\":{"
            + "\"body\":{\"quad\":{\"x\":3,\"y\":4,\"width\":16,\"height\":16}},"
            + "\"skin\":{\"quad\":{\"type\":\"mesh\",\"uvs\":[0,0,1,0,1,1,0,1],\"triangles\":[0,1,2,0,2,3],\"hull\":4,"
            + "\"vertices\":[1,0,-8,8,1, 1,0,8,8,1, 1,1,-8,2,1, 1,1,-8,18,1],\"width\":16,\"height\":16}}"
            + "}}],"
            + "\"animations\":{\"idle\":{"
            + "\"bones\":{"
            + "\"arm\":{\"rotate\":[{\"time\":0,\"angle\":0,\"curve\":0.25,\"c3\":0.75},{\"time\":1,\"angle\":90}]},"
            + "\"root\":{\"translate\":[{\"time\":0},{\"time\":1,\"x\":10,\"y\":0}]}"
            + "},"
            + "\"slots\":{\"body\":{\"attachment\":[{\"time\":0.5,\"name\":null}]}}"
            + "}}"
            + "}";

        public const string AtlasText = "\nfixture.png\nsize: 32,16\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\n"
            + "quad\n  rotate: false\n  xy: 0, 0\n  size: 16, 16\n  orig: 16, 16\n  offset: 0, 0\n  index: -1\n";
    }
}
