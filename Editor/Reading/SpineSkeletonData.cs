using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpineTo2DAnimation.Editor.Reading
{
    public sealed class SpineBoneData
    {
        public int Index;
        public string Name;
        public SpineBoneData Parent;
        public float Length;
        public float X;
        public float Y;
        public float Rotation;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float ShearX;
        public float ShearY;
        public string TransformMode = "normal";
    }

    public sealed class SpineSlotData
    {
        public int Index;
        public string Name;
        public SpineBoneData Bone;
        public string Attachment;
        public SpineColor Color = SpineColor.White;
        public string Blend = "normal";
    }

    public enum SpineAttachmentType
    {
        Region,
        Mesh,
        LinkedMesh,
        Unsupported
    }

    public sealed class SpineAttachmentData
    {
        public string Name;
        public string SlotName;
        public SpineAttachmentType Type;
        public string RawType;
        /// <summary>Atlas region name: the "path" attribute when present, the attachment name otherwise.</summary>
        public string Path;
        public SpineColor Color = SpineColor.White;

        // Region attachment.
        public float X;
        public float Y;
        public float Rotation;
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public float Width;
        public float Height;

        // Mesh attachment (uvs relative to the image, v pointing down).
        public float[] Uvs;
        public int[] Triangles;
        public float[] Vertices;
        public int[] Edges;
        public int Hull;

        // Linked mesh.
        public string ParentMesh;
        public string ParentSkin;

        /// <summary>A mesh is weighted when its vertex array stores bone influences rather than plain x/y pairs.</summary>
        public bool IsWeighted => Uvs != null && Vertices != null && Vertices.Length != Uvs.Length;
    }

    public sealed class SpineSkinData
    {
        public string Name;
        /// <summary>slot name → attachment name → attachment.</summary>
        public Dictionary<string, Dictionary<string, SpineAttachmentData>> Attachments =
            new Dictionary<string, Dictionary<string, SpineAttachmentData>>(StringComparer.Ordinal);

        public SpineAttachmentData Find(string slotName, string attachmentName)
        {
            return slotName != null
                && attachmentName != null
                && Attachments.TryGetValue(slotName, out Dictionary<string, SpineAttachmentData> slotAttachments)
                && slotAttachments.TryGetValue(attachmentName, out SpineAttachmentData attachment)
                    ? attachment
                    : null;
        }
    }

    /// <summary>Bezier description of a Spine 3.8 keyframe transition toward the next keyframe.</summary>
    public struct SpineCurve
    {
        public bool Stepped;
        public bool IsBezier;
        public float C1;
        public float C2;
        public float C3;
        public float C4;

        public static readonly SpineCurve Linear = new SpineCurve();
    }

    public sealed class SpineKeyframe
    {
        public float Time;
        public float Value1;
        public float Value2;
        public string Name;
        public SpineColor Color;
        public SpineCurve Curve;
    }

    public sealed class SpineTimeline
    {
        public string Target;
        public string Property;
        public List<SpineKeyframe> Keys = new List<SpineKeyframe>();
    }

    public sealed class SpineAnimationData
    {
        public string Name;
        public List<SpineTimeline> BoneTimelines = new List<SpineTimeline>();
        public List<SpineTimeline> SlotTimelines = new List<SpineTimeline>();
        public List<string> UnsupportedTimelines = new List<string>();

        public float Duration
        {
            get
            {
                float duration = 0f;
                foreach (SpineTimeline timeline in BoneTimelines)
                    duration = Math.Max(duration, LastTime(timeline));
                foreach (SpineTimeline timeline in SlotTimelines)
                    duration = Math.Max(duration, LastTime(timeline));
                return duration;
            }
        }

        private static float LastTime(SpineTimeline timeline)
        {
            return timeline.Keys.Count == 0 ? 0f : timeline.Keys[timeline.Keys.Count - 1].Time;
        }
    }

    public struct SpineColor
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public static readonly SpineColor White = new SpineColor { R = 1f, G = 1f, B = 1f, A = 1f };

        public static SpineColor Parse(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return White;

            if (hex.Length != 8 && hex.Length != 6)
                throw new FormatException($"Invalid Spine color '{hex}'.");

            return new SpineColor
            {
                R = Channel(hex, 0),
                G = Channel(hex, 2),
                B = Channel(hex, 4),
                A = hex.Length == 8 ? Channel(hex, 6) : 1f
            };
        }

        public SpineColor Multiply(SpineColor other)
        {
            return new SpineColor { R = R * other.R, G = G * other.G, B = B * other.B, A = A * other.A };
        }

        private static float Channel(string hex, int offset)
        {
            return int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
        }
    }

    /// <summary>
    /// Spine 3.8 skeleton data read from the exported JSON. Skins are accepted both as the 3.8 array
    /// and as the older name → attachments object.
    /// </summary>
    public sealed class SpineSkeletonData
    {
        public string SpineVersion;
        public List<SpineBoneData> Bones = new List<SpineBoneData>();
        public List<SpineSlotData> Slots = new List<SpineSlotData>();
        public List<SpineSkinData> Skins = new List<SpineSkinData>();
        public List<SpineAnimationData> Animations = new List<SpineAnimationData>();
        public List<string> UnsupportedFeatures = new List<string>();

        public SpineBoneData FindBone(string name) => Bones.Find(bone => bone.Name == name);
        public SpineSlotData FindSlot(string name) => Slots.Find(slot => slot.Name == name);
        public SpineSkinData FindSkin(string name) => Skins.Find(skin => skin.Name == name);
        public SpineSkinData DefaultSkin => FindSkin("default") ?? (Skins.Count > 0 ? Skins[0] : null);

        public static SpineSkeletonData Parse(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>
                ?? throw new FormatException("Spine skeleton JSON must be an object.");

            var data = new SpineSkeletonData();
            if (Object(root, "skeleton") is Dictionary<string, object> skeleton)
                data.SpineVersion = String(skeleton, "spine");

            ReadBones(root, data);
            ReadSlots(root, data);
            ReadSkins(root, data);
            ReadAnimations(root, data);

            foreach (string section in new[] { "ik", "transform", "path" })
            {
                if (Array(root, section) is List<object> constraints && constraints.Count > 0)
                    data.UnsupportedFeatures.Add($"{constraints.Count} {section} constraint(s)");
            }

            return data;
        }

        private static void ReadBones(Dictionary<string, object> root, SpineSkeletonData data)
        {
            foreach (object item in Array(root, "bones") ?? new List<object>())
            {
                var map = (Dictionary<string, object>)item;
                var bone = new SpineBoneData
                {
                    Index = data.Bones.Count,
                    Name = String(map, "name"),
                    Length = Float(map, "length", 0f),
                    X = Float(map, "x", 0f),
                    Y = Float(map, "y", 0f),
                    Rotation = Float(map, "rotation", 0f),
                    ScaleX = Float(map, "scaleX", 1f),
                    ScaleY = Float(map, "scaleY", 1f),
                    ShearX = Float(map, "shearX", 0f),
                    ShearY = Float(map, "shearY", 0f),
                    TransformMode = String(map, "transform") ?? "normal"
                };

                string parentName = String(map, "parent");
                if (parentName != null)
                {
                    bone.Parent = data.FindBone(parentName)
                        ?? throw new FormatException($"Bone '{bone.Name}' references unknown parent '{parentName}'.");
                }

                if (bone.ShearX != 0f || bone.ShearY != 0f)
                    data.UnsupportedFeatures.Add($"bone '{bone.Name}' setup shear ignored");

                if (bone.TransformMode != "normal")
                    data.UnsupportedFeatures.Add($"bone '{bone.Name}' transform mode '{bone.TransformMode}' treated as normal");

                data.Bones.Add(bone);
            }
        }

        private static void ReadSlots(Dictionary<string, object> root, SpineSkeletonData data)
        {
            foreach (object item in Array(root, "slots") ?? new List<object>())
            {
                var map = (Dictionary<string, object>)item;
                string boneName = String(map, "bone");
                var slot = new SpineSlotData
                {
                    Index = data.Slots.Count,
                    Name = String(map, "name"),
                    Bone = data.FindBone(boneName) ?? throw new FormatException($"Slot references unknown bone '{boneName}'."),
                    Attachment = String(map, "attachment"),
                    Color = SpineColor.Parse(String(map, "color")),
                    Blend = String(map, "blend") ?? "normal"
                };

                if (slot.Blend != "normal")
                    data.UnsupportedFeatures.Add($"slot '{slot.Name}' blend mode '{slot.Blend}' rendered as normal");

                data.Slots.Add(slot);
            }
        }

        private static void ReadSkins(Dictionary<string, object> root, SpineSkeletonData data)
        {
            if (!root.TryGetValue("skins", out object skinsValue) || skinsValue == null)
                return;

            if (skinsValue is List<object> skinList)
            {
                foreach (object item in skinList)
                {
                    var map = (Dictionary<string, object>)item;
                    data.Skins.Add(ReadSkin(String(map, "name"), Object(map, "attachments"), data));
                }

                return;
            }

            foreach (KeyValuePair<string, object> pair in (Dictionary<string, object>)skinsValue)
                data.Skins.Add(ReadSkin(pair.Key, pair.Value as Dictionary<string, object>, data));
        }

        private static SpineSkinData ReadSkin(string name, Dictionary<string, object> slots, SpineSkeletonData data)
        {
            var skin = new SpineSkinData { Name = name };
            if (slots == null)
                return skin;

            foreach (KeyValuePair<string, object> slotPair in slots)
            {
                var attachments = new Dictionary<string, SpineAttachmentData>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> attachmentPair in (Dictionary<string, object>)slotPair.Value)
                {
                    SpineAttachmentData attachment = ReadAttachment(slotPair.Key, attachmentPair.Key, (Dictionary<string, object>)attachmentPair.Value);
                    if (attachment.Type == SpineAttachmentType.Unsupported)
                        data.UnsupportedFeatures.Add($"attachment '{attachment.Name}' of type '{attachment.RawType}' skipped");

                    attachments[attachmentPair.Key] = attachment;
                }

                skin.Attachments[slotPair.Key] = attachments;
            }

            return skin;
        }

        private static SpineAttachmentData ReadAttachment(string slotName, string name, Dictionary<string, object> map)
        {
            string type = String(map, "type") ?? "region";
            var attachment = new SpineAttachmentData
            {
                Name = String(map, "name") ?? name,
                SlotName = slotName,
                RawType = type,
                Color = SpineColor.Parse(String(map, "color")),
                Width = Float(map, "width", 0f),
                Height = Float(map, "height", 0f)
            };
            attachment.Path = String(map, "path") ?? attachment.Name;

            switch (type)
            {
                case "region":
                    attachment.Type = SpineAttachmentType.Region;
                    attachment.X = Float(map, "x", 0f);
                    attachment.Y = Float(map, "y", 0f);
                    attachment.Rotation = Float(map, "rotation", 0f);
                    attachment.ScaleX = Float(map, "scaleX", 1f);
                    attachment.ScaleY = Float(map, "scaleY", 1f);
                    break;
                case "mesh":
                    attachment.Type = SpineAttachmentType.Mesh;
                    attachment.Uvs = Floats(map, "uvs");
                    attachment.Vertices = Floats(map, "vertices");
                    attachment.Triangles = Ints(map, "triangles");
                    attachment.Edges = Ints(map, "edges");
                    attachment.Hull = (int)Float(map, "hull", 0f);
                    break;
                case "linkedmesh":
                    attachment.Type = SpineAttachmentType.LinkedMesh;
                    attachment.ParentMesh = String(map, "parent");
                    attachment.ParentSkin = String(map, "skin");
                    break;
                default:
                    attachment.Type = SpineAttachmentType.Unsupported;
                    break;
            }

            return attachment;
        }

        private static void ReadAnimations(Dictionary<string, object> root, SpineSkeletonData data)
        {
            if (!(Object(root, "animations") is Dictionary<string, object> animations))
                return;

            foreach (KeyValuePair<string, object> animationPair in animations)
            {
                var map = (Dictionary<string, object>)animationPair.Value;
                var animation = new SpineAnimationData { Name = animationPair.Key };

                if (Object(map, "bones") is Dictionary<string, object> bones)
                {
                    foreach (KeyValuePair<string, object> bonePair in bones)
                    {
                        foreach (KeyValuePair<string, object> timelinePair in (Dictionary<string, object>)bonePair.Value)
                        {
                            if (timelinePair.Key != "rotate" && timelinePair.Key != "translate" && timelinePair.Key != "scale")
                            {
                                animation.UnsupportedTimelines.Add($"bone '{bonePair.Key}' {timelinePair.Key}");
                                continue;
                            }

                            animation.BoneTimelines.Add(ReadTimeline(bonePair.Key, timelinePair.Key, (List<object>)timelinePair.Value));
                        }
                    }
                }

                if (Object(map, "slots") is Dictionary<string, object> slots)
                {
                    foreach (KeyValuePair<string, object> slotPair in slots)
                    {
                        foreach (KeyValuePair<string, object> timelinePair in (Dictionary<string, object>)slotPair.Value)
                        {
                            if (timelinePair.Key != "attachment" && timelinePair.Key != "color")
                            {
                                animation.UnsupportedTimelines.Add($"slot '{slotPair.Key}' {timelinePair.Key}");
                                continue;
                            }

                            animation.SlotTimelines.Add(ReadTimeline(slotPair.Key, timelinePair.Key, (List<object>)timelinePair.Value));
                        }
                    }
                }

                foreach (string section in new[] { "ik", "transform", "path", "deform", "drawOrder", "draworder", "events" })
                {
                    if (map.ContainsKey(section))
                        animation.UnsupportedTimelines.Add(section);
                }

                data.Animations.Add(animation);
            }
        }

        private static SpineTimeline ReadTimeline(string target, string property, List<object> keys)
        {
            var timeline = new SpineTimeline { Target = target, Property = property };
            float defaultValue = property == "scale" ? 1f : 0f;

            foreach (object item in keys)
            {
                var map = (Dictionary<string, object>)item;
                var key = new SpineKeyframe
                {
                    Time = Float(map, "time", 0f),
                    Curve = ReadCurve(map)
                };

                switch (property)
                {
                    case "rotate":
                        key.Value1 = Float(map, "angle", 0f);
                        break;
                    case "translate":
                    case "scale":
                        key.Value1 = Float(map, "x", defaultValue);
                        key.Value2 = Float(map, "y", defaultValue);
                        break;
                    case "attachment":
                        key.Name = String(map, "name");
                        break;
                    case "color":
                        key.Color = SpineColor.Parse(String(map, "color"));
                        break;
                }

                timeline.Keys.Add(key);
            }

            return timeline;
        }

        private static SpineCurve ReadCurve(Dictionary<string, object> map)
        {
            if (!map.TryGetValue("curve", out object curve) || curve == null)
                return SpineCurve.Linear;

            if (curve is string text)
                return text == "stepped" ? new SpineCurve { Stepped = true } : SpineCurve.Linear;

            if (curve is double c1)
            {
                return new SpineCurve
                {
                    IsBezier = true,
                    C1 = (float)c1,
                    C2 = Float(map, "c2", 0f),
                    C3 = Float(map, "c3", 1f),
                    C4 = Float(map, "c4", 1f)
                };
            }

            // Spine 3.7 and earlier: "curve": [c1, c2, c3, c4].
            if (curve is List<object> values && values.Count == 4)
            {
                return new SpineCurve
                {
                    IsBezier = true,
                    C1 = Convert.ToSingle(values[0], CultureInfo.InvariantCulture),
                    C2 = Convert.ToSingle(values[1], CultureInfo.InvariantCulture),
                    C3 = Convert.ToSingle(values[2], CultureInfo.InvariantCulture),
                    C4 = Convert.ToSingle(values[3], CultureInfo.InvariantCulture)
                };
            }

            return SpineCurve.Linear;
        }

        private static Dictionary<string, object> Object(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;
        }

        private static List<object> Array(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out object value) ? value as List<object> : null;
        }

        private static string String(Dictionary<string, object> map, string key)
        {
            return map.TryGetValue(key, out object value) ? value as string : null;
        }

        private static float Float(Dictionary<string, object> map, string key, float defaultValue)
        {
            return map.TryGetValue(key, out object value) && value is double number ? (float)number : defaultValue;
        }

        private static float[] Floats(Dictionary<string, object> map, string key)
        {
            List<object> values = Array(map, key);
            if (values == null)
                return null;

            var result = new float[values.Count];
            for (int index = 0; index < values.Count; index++)
                result[index] = (float)(double)values[index];

            return result;
        }

        private static int[] Ints(Dictionary<string, object> map, string key)
        {
            List<object> values = Array(map, key);
            if (values == null)
                return null;

            var result = new int[values.Count];
            for (int index = 0; index < values.Count; index++)
                result[index] = (int)(double)values[index];

            return result;
        }
    }
}
