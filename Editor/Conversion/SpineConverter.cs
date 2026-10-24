using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SpineTo2DAnimation.Editor.Reading;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D.Animation;

namespace SpineTo2DAnimation.Editor.Conversion
{
    public sealed class SpineConversionRequest
    {
        /// <summary>Absolute path of the exported Spine skeleton JSON.</summary>
        public string SkeletonJsonPath;
        /// <summary>Absolute path of the Spine atlas (.atlas or .atlas.txt); pages are read next to it.</summary>
        public string AtlasPath;
        /// <summary>Project folder receiving the generated assets, e.g. "Assets/Characters/Duck". Replaced on each run.</summary>
        public string OutputFolder;
        /// <summary>Name of the generated prefab and controller; defaults to the JSON file name.</summary>
        public string Name;
        public float PixelsPerUnit = 100f;
        public float FrameRate = 30f;
    }

    public sealed class SpineConversionResult
    {
        public string PrefabPath;
        public string ControllerPath;
        public List<string> ClipPaths = new List<string>();
        public List<string> Notes = new List<string>();
        public string ReportPath;
        public int BoneCount;
        public int RegionAttachmentCount;
        public int MeshAttachmentCount;
        /// <summary>Assets written by this run; anything else left in the output folder is removed.</summary>
        public HashSet<string> WrittenPaths = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Converts a Spine 3.8 export into a Unity 2D Animation prefab. Supported: bones (normal inheritance),
    /// slots and draw order, region and mesh attachments (weighted or not), rotate/translate/scale bone
    /// timelines, attachment/color slot timelines. Everything else is listed in the conversion report.
    /// </summary>
    public static class SpineConverter
    {
        private const float MaxAcceptedFitResidual = 1f;

        private sealed class AttachmentInstance
        {
            public string Key;
            public string Path;
            public GameObject GameObject;
            public SpriteRenderer Renderer;
            public SpineColor AttachmentColor;
        }

        private sealed class SlotInstance
        {
            public SpineSlotData Data;
            public List<AttachmentInstance> Attachments = new List<AttachmentInstance>();
        }

        public static SpineConversionResult Convert(SpineConversionRequest request)
        {
            Validate(request);

            string name = string.IsNullOrWhiteSpace(request.Name)
                ? StripSpineExtensions(Path.GetFileName(request.SkeletonJsonPath))
                : request.Name;
            SpineSkeletonData skeleton = SpineSkeletonData.Parse(File.ReadAllText(request.SkeletonJsonPath));
            SpineAtlas atlas = SpineAtlas.Parse(File.ReadAllText(request.AtlasPath));

            var result = new SpineConversionResult { BoneCount = skeleton.Bones.Count };
            result.Notes.AddRange(skeleton.UnsupportedFeatures);
            if (skeleton.SpineVersion != null && !skeleton.SpineVersion.StartsWith("3.", StringComparison.Ordinal))
                result.Notes.Add($"Spine export version {skeleton.SpineVersion} is outside the tested 3.x format.");

            EnsureFolder(request.OutputFolder);
            string spritesFolder = request.OutputFolder + "/Sprites";
            EnsureFolder(spritesFolder);

            var root = new GameObject(name);
            try
            {
                var solver = new SpinePoseSolver(skeleton);
                SpineLocalPose[] setupPose = solver.SolvedPose(null, 0f, out Matrix2D[] world);
                if (solver.HasConstraints)
                    result.Notes.Add($"{skeleton.IkConstraints.Count} IK constraint(s) solved in the setup pose and baked into bone rotations.");

                Transform[] bones = BuildBones(skeleton, setupPose, root.transform, request.PixelsPerUnit);
                CheckSetupScale(skeleton, world, result);

                using (var extractor = new RegionExtractor(Path.GetDirectoryName(request.AtlasPath)))
                {
                    foreach (SpineAtlasPage page in atlas.Pages)
                    {
                        if (extractor.IsPremultiplied(page))
                            result.Notes.Add($"Page '{page.FileName}' looks premultiplied: colors were un-premultiplied.");
                    }

                    List<SlotInstance> slots = BuildSlots(skeleton, atlas, extractor, root.transform, bones, world, spritesFolder, request, result);
                    BuildAnimations(skeleton, solver, slots, root.transform, bones, request, result);
                }

                root.AddComponent<SortingGroup>();
                Animator animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = CreateController(request.OutputFolder, name, result);

                result.PrefabPath = $"{request.OutputFolder}/{name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, result.PrefabPath);
                result.WrittenPaths.Add(result.PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            result.ReportPath = $"{request.OutputFolder}/conversion-report.md";
            File.WriteAllText(SpriteAssetWriter.ToAbsolutePath(result.ReportPath), FormatReport(name, request, skeleton, result));
            AssetDatabase.ImportAsset(result.ReportPath);
            result.WrittenPaths.Add(result.ReportPath);
            RemoveStaleAssets(request.OutputFolder, result.WrittenPaths);
            AssetDatabase.SaveAssets();
            return result;
        }

        private static void Validate(SpineConversionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!File.Exists(request.SkeletonJsonPath))
                throw new FileNotFoundException("Spine skeleton JSON not found.", request.SkeletonJsonPath);
            if (!File.Exists(request.AtlasPath))
                throw new FileNotFoundException("Spine atlas not found.", request.AtlasPath);
            if (string.IsNullOrWhiteSpace(request.OutputFolder) || !request.OutputFolder.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("OutputFolder must be a project folder under Assets/.", nameof(request));
            if (request.PixelsPerUnit <= 0f || request.FrameRate <= 0f)
                throw new ArgumentException("PixelsPerUnit and FrameRate must be positive.", nameof(request));

            request.OutputFolder = request.OutputFolder.Replace('\\', '/').TrimEnd('/');
        }

        private static Transform[] BuildBones(SpineSkeletonData skeleton, SpineLocalPose[] pose, Transform root, float ppu)
        {
            var transforms = new Transform[skeleton.Bones.Count];
            foreach (SpineBoneData bone in skeleton.Bones)
            {
                SpineLocalPose local = pose[bone.Index];
                var gameObject = new GameObject(SafeName(bone.Name));
                Transform parent = bone.Parent == null ? root : transforms[bone.Parent.Index];
                gameObject.transform.SetParent(parent, false);
                gameObject.transform.localPosition = new Vector3(local.X / ppu, local.Y / ppu, 0f);
                gameObject.transform.localRotation = Quaternion.Euler(0f, 0f, local.Rotation);
                gameObject.transform.localScale = new Vector3(local.ScaleX, local.ScaleY, 1f);
                transforms[bone.Index] = gameObject.transform;
            }

            return transforms;
        }

        private static void CheckSetupScale(SpineSkeletonData skeleton, Matrix2D[] world, SpineConversionResult result)
        {
            foreach (SpineBoneData bone in skeleton.Bones)
            {
                Matrix2D matrix = world[bone.Index];
                if (Mathf.Abs(matrix.ScaleX - 1f) > 1e-3f || Mathf.Abs(matrix.ScaleY - 1f) > 1e-3f)
                    result.Notes.Add($"Bone '{bone.Name}' has a non-unit setup world scale; meshes bound to it may be distorted (SpriteSkin bind poses carry no scale).");
            }
        }

        private static List<SlotInstance> BuildSlots(
            SpineSkeletonData skeleton,
            SpineAtlas atlas,
            RegionExtractor extractor,
            Transform root,
            Transform[] bones,
            Matrix2D[] world,
            string spritesFolder,
            SpineConversionRequest request,
            SpineConversionResult result)
        {
            SpineSkinData skin = skeleton.DefaultSkin;
            foreach (SpineSkinData other in skeleton.Skins)
            {
                if (other != skin)
                    result.Notes.Add($"Skin '{other.Name}' ignored: only the default skin is converted.");
            }

            var meshContainer = new GameObject("Meshes").transform;
            meshContainer.SetParent(root, false);
            var regionSprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var slots = new List<SlotInstance>();

            foreach (SpineSlotData slot in skeleton.Slots)
            {
                var instance = new SlotInstance { Data = slot };
                slots.Add(instance);
                if (skin == null || !skin.Attachments.TryGetValue(slot.Name, out Dictionary<string, SpineAttachmentData> attachments))
                    continue;

                foreach (KeyValuePair<string, SpineAttachmentData> pair in attachments)
                {
                    SpineAttachmentData attachment = pair.Value;
                    SpineAttachmentData geometry = attachment;
                    if (attachment.Type == SpineAttachmentType.LinkedMesh)
                    {
                        SpineSkinData parentSkin = attachment.ParentSkin != null ? skeleton.FindSkin(attachment.ParentSkin) : skin;
                        geometry = parentSkin?.Find(slot.Name, attachment.ParentMesh);
                        if (geometry == null || geometry.Type != SpineAttachmentType.Mesh)
                        {
                            result.Notes.Add($"Linked mesh '{pair.Key}' skipped: parent mesh '{attachment.ParentMesh}' not found.");
                            continue;
                        }
                    }

                    if (geometry.Type != SpineAttachmentType.Region && geometry.Type != SpineAttachmentType.Mesh)
                        continue;

                    SpineAtlasRegion region = atlas.FindRegion(attachment.Path);
                    if (region == null)
                    {
                        result.Notes.Add($"Attachment '{pair.Key}' skipped: atlas region '{attachment.Path}' not found.");
                        continue;
                    }

                    if (region.IsWhitespaceStripped && geometry.Type == SpineAttachmentType.Mesh)
                        result.Notes.Add($"Region '{region.Name}' is whitespace-stripped: mesh UVs are mapped on the original image size.");

                    string objectName = SafeName($"{slot.Name} : {pair.Key}");
                    var attachmentObject = new GameObject(objectName);
                    SpriteRenderer renderer = attachmentObject.AddComponent<SpriteRenderer>();
                    renderer.sortingOrder = slot.Index;
                    renderer.color = ToColor(slot.Color.Multiply(attachment.Color));

                    if (geometry.Type == SpineAttachmentType.Region)
                    {
                        attachmentObject.transform.SetParent(bones[slot.Bone.Index], false);
                        renderer.sprite = GetRegionSprite(region, extractor, spritesFolder, request.PixelsPerUnit, regionSprites, result);
                        float widthScale = geometry.Width > 0f ? geometry.Width / region.OriginalWidth : 1f;
                        float heightScale = geometry.Height > 0f ? geometry.Height / region.OriginalHeight : 1f;
                        attachmentObject.transform.localPosition = new Vector3(geometry.X / request.PixelsPerUnit, geometry.Y / request.PixelsPerUnit, 0f);
                        attachmentObject.transform.localRotation = Quaternion.Euler(0f, 0f, geometry.Rotation);
                        attachmentObject.transform.localScale = new Vector3(geometry.ScaleX * widthScale, geometry.ScaleY * heightScale, 1f);
                        result.RegionAttachmentCount++;
                    }
                    else
                    {
                        attachmentObject.transform.SetParent(meshContainer, false);
                        BuildSkinnedMesh(skeleton, slot, pair.Key, geometry, region, extractor, renderer, bones, world, spritesFolder, request, result);
                        result.MeshAttachmentCount++;
                    }

                    attachmentObject.SetActive(pair.Key == slot.Attachment);
                    instance.Attachments.Add(new AttachmentInstance
                    {
                        Key = pair.Key,
                        Path = RelativePath(root, attachmentObject.transform),
                        GameObject = attachmentObject,
                        Renderer = renderer,
                        AttachmentColor = attachment.Color
                    });
                }
            }

            return slots;
        }

        private static Sprite GetRegionSprite(
            SpineAtlasRegion region,
            RegionExtractor extractor,
            string spritesFolder,
            float ppu,
            Dictionary<string, Sprite> cache,
            SpineConversionResult result)
        {
            if (cache.TryGetValue(region.Name, out Sprite cached))
                return cached;

            Color32[] pixels = extractor.Extract(region, out int width, out int height);
            string spritePath = $"{spritesFolder}/{SafeFileName(region.Name)}.png";
            Sprite sprite = SpriteAssetWriter.WriteSprite(spritePath, pixels, width, height, ppu, null);
            result.WrittenPaths.Add(spritePath);
            cache[region.Name] = sprite;
            return sprite;
        }

        private static void BuildSkinnedMesh(
            SpineSkeletonData skeleton,
            SpineSlotData slot,
            string attachmentKey,
            SpineAttachmentData mesh,
            SpineAtlasRegion region,
            RegionExtractor extractor,
            SpriteRenderer renderer,
            Transform[] bones,
            Matrix2D[] world,
            string spritesFolder,
            SpineConversionRequest request,
            SpineConversionResult result)
        {
            int vertexCount = mesh.Uvs.Length / 2;
            var spritePoints = new Vector2[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                // Spine UVs have their origin at the top-left of the image; sprite rects at the bottom-left.
                spritePoints[vertex] = new Vector2(
                    mesh.Uvs[vertex * 2] * region.OriginalWidth,
                    (1f - mesh.Uvs[vertex * 2 + 1]) * region.OriginalHeight);
            }

            var influences = ReadInfluences(mesh, slot, vertexCount);
            var worldPoints = new Vector2[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                Vector2 point = Vector2.zero;
                foreach (Influence influence in influences[vertex])
                    point += world[influence.Bone].Apply(influence.Local) * influence.Weight;
                worldPoints[vertex] = point;
            }

            Similarity2D fit = Similarity2D.Fit(spritePoints, worldPoints);
            string label = $"{slot.Name}/{attachmentKey}";
            result.Notes.Add(FormattableString.Invariant($"Mesh '{label}': {vertexCount} vertices, fit scale {fit.Scale:0.####}, residual {fit.MaxResidual:0.###} px."));
            if (fit.MaxResidual > MaxAcceptedFitResidual)
                result.Notes.Add(FormattableString.Invariant($"WARNING mesh '{label}': setup vertices deviate from the image layout by {fit.MaxResidual:0.###} px; the setup pose was deformed in Spine and is approximated."));

            var usedBones = new List<int>();
            foreach (List<Influence> vertexInfluences in influences)
            {
                foreach (Influence influence in vertexInfluences)
                {
                    if (!usedBones.Contains(influence.Bone))
                        usedBones.Add(influence.Bone);
                }
            }

            var spec = new SpriteMeshSpec
            {
                Vertices = spritePoints,
                Weights = new BoneWeight[vertexCount],
                Indices = mesh.Triangles,
                Edges = BuildHullEdges(mesh.Hull, vertexCount)
            };

            foreach (int boneIndex in usedBones)
            {
                Matrix2D boneWorld = world[boneIndex];
                spec.Bones.Add(new SpriteBoneSpec
                {
                    Name = skeleton.Bones[boneIndex].Name,
                    Position = fit.ApplyInverse(new Vector2(boneWorld.X, boneWorld.Y)),
                    RotationDegrees = boneWorld.RotationDegrees - fit.RotationDegrees,
                    Length = skeleton.Bones[boneIndex].Length / fit.Scale
                });
            }

            int truncated = 0;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                List<Influence> ordered = influences[vertex].OrderByDescending(influence => influence.Weight).ToList();
                if (ordered.Count > 4)
                    truncated++;

                spec.Weights[vertex] = ToBoneWeight(ordered.Take(4).ToList(), usedBones);
            }

            if (truncated > 0)
                result.Notes.Add($"Mesh '{label}': {truncated} vertices had more than 4 bone influences; the 4 strongest were kept and renormalized.");

            string spritePath = $"{spritesFolder}/{SafeFileName(slot.Name)}__{SafeFileName(attachmentKey)}__mesh.png";
            Color32[] pixels = extractor.Extract(region, out int width, out int height);
            renderer.sprite = SpriteAssetWriter.WriteSprite(spritePath, pixels, width, height, request.PixelsPerUnit / fit.Scale, spec);
            result.WrittenPaths.Add(spritePath);

            SpriteSkin skinComponent = renderer.gameObject.AddComponent<SpriteSkin>();
            skinComponent.SetRootBone(bones[0]);
            skinComponent.SetBoneTransforms(usedBones.Select(index => bones[index]).ToArray());
        }

        private struct Influence
        {
            public int Bone;
            public Vector2 Local;
            public float Weight;
        }

        private static List<Influence>[] ReadInfluences(SpineAttachmentData mesh, SpineSlotData slot, int vertexCount)
        {
            var influences = new List<Influence>[vertexCount];
            if (!mesh.IsWeighted)
            {
                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    influences[vertex] = new List<Influence>
                    {
                        new Influence
                        {
                            Bone = slot.Bone.Index,
                            Local = new Vector2(mesh.Vertices[vertex * 2], mesh.Vertices[vertex * 2 + 1]),
                            Weight = 1f
                        }
                    };
                }

                return influences;
            }

            int cursor = 0;
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                int boneCount = (int)mesh.Vertices[cursor++];
                var list = new List<Influence>(boneCount);
                for (int influence = 0; influence < boneCount; influence++)
                {
                    list.Add(new Influence
                    {
                        Bone = (int)mesh.Vertices[cursor],
                        Local = new Vector2(mesh.Vertices[cursor + 1], mesh.Vertices[cursor + 2]),
                        Weight = mesh.Vertices[cursor + 3]
                    });
                    cursor += 4;
                }

                influences[vertex] = list;
            }

            return influences;
        }

        private static BoneWeight ToBoneWeight(List<Influence> influences, List<int> usedBones)
        {
            float total = influences.Sum(influence => influence.Weight);
            if (total <= 0f)
                total = 1f;

            var weight = new BoneWeight();
            for (int slot = 0; slot < influences.Count; slot++)
            {
                int index = usedBones.IndexOf(influences[slot].Bone);
                float value = influences[slot].Weight / total;
                switch (slot)
                {
                    case 0: weight.boneIndex0 = index; weight.weight0 = value; break;
                    case 1: weight.boneIndex1 = index; weight.weight1 = value; break;
                    case 2: weight.boneIndex2 = index; weight.weight2 = value; break;
                    case 3: weight.boneIndex3 = index; weight.weight3 = value; break;
                }
            }

            return weight;
        }

        private static Vector2Int[] BuildHullEdges(int hull, int vertexCount)
        {
            int count = Mathf.Clamp(hull, 0, vertexCount);
            var edges = new Vector2Int[count];
            for (int index = 0; index < count; index++)
                edges[index] = new Vector2Int(index, (index + 1) % count);

            return edges;
        }

        private static void BuildAnimations(
            SpineSkeletonData skeleton,
            SpinePoseSolver solver,
            List<SlotInstance> slots,
            Transform root,
            Transform[] bones,
            SpineConversionRequest request,
            SpineConversionResult result)
        {
            foreach (SpineAnimationData animation in skeleton.Animations)
            {
                foreach (string unsupported in animation.UnsupportedTimelines)
                    result.Notes.Add($"Animation '{animation.Name}': {unsupported} timeline ignored.");

                AnimationClip clip = SpineAnimationBaker.Bake(animation, skeleton, solver, slots.Select(ToBakerSlot).ToList(), root, bones, request.PixelsPerUnit, request.FrameRate);
                string clipPath = $"{request.OutputFolder}/{SafeFileName(animation.Name)}.anim";
                SaveClip(clip, clipPath);
                result.ClipPaths.Add(clipPath);
                result.WrittenPaths.Add(clipPath);
            }
        }

        private static SpineAnimationBaker.SlotBinding ToBakerSlot(SlotInstance slot)
        {
            return new SpineAnimationBaker.SlotBinding
            {
                Slot = slot.Data,
                Attachments = slot.Attachments
                    .Select(attachment => new SpineAnimationBaker.AttachmentBinding
                    {
                        Key = attachment.Key,
                        Path = attachment.Path,
                        AttachmentColor = attachment.AttachmentColor
                    })
                    .ToList()
            };
        }

        /// <summary>Overwrites an existing clip in place so that its GUID, and every reference to it, survives a reconversion.</summary>
        private static void SaveClip(AnimationClip clip, string clipPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, clipPath);
                return;
            }

            EditorUtility.CopySerialized(clip, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(clip);
        }

        /// <summary>Reuses the existing controller when it already plays exactly these clips, so reconversions leave it untouched.</summary>
        private static AnimatorController CreateController(string folder, string name, SpineConversionResult result)
        {
            List<string> clipPaths = result.ClipPaths;
            string controllerPath = $"{folder}/{SafeFileName(name)}.controller";
            result.WrittenPaths.Add(controllerPath);
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (existing != null && ControllerMatches(existing, clipPaths))
                return existing;

            if (existing != null)
                AssetDatabase.DeleteAsset(controllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState preferred = null;

            foreach (string clipPath in clipPaths)
            {
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                AnimatorState state = stateMachine.AddState(clip.name);
                state.motion = clip;
                if (preferred == null && clip.name.StartsWith("idle", StringComparison.OrdinalIgnoreCase))
                    preferred = state;
            }

            if (preferred != null)
                stateMachine.defaultState = preferred;

            return controller;
        }

        private static bool ControllerMatches(AnimatorController controller, List<string> clipPaths)
        {
            var expected = new HashSet<string>(clipPaths, StringComparer.Ordinal);
            var actual = new HashSet<string>(
                controller.layers[0].stateMachine.states.Select(state => AssetDatabase.GetAssetPath(state.state.motion)),
                StringComparer.Ordinal);
            return expected.SetEquals(actual);
        }

        /// <summary>Deletes assets of a previous run that this conversion no longer produces.</summary>
        private static void RemoveStaleAssets(string folder, HashSet<string> written)
        {
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path) && !written.Contains(path))
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string FormatReport(string name, SpineConversionRequest request, SpineSkeletonData skeleton, SpineConversionResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"# Spine → Unity 2D Animation: {name}");
            builder.AppendLine();
            builder.AppendLine($"- Source skeleton: `{Path.GetFileName(request.SkeletonJsonPath)}` (Spine {skeleton.SpineVersion ?? "unknown"})");
            builder.AppendLine($"- Atlas: `{Path.GetFileName(request.AtlasPath)}`");
            builder.AppendLine(FormattableString.Invariant($"- Pixels per unit: {request.PixelsPerUnit}, sampling: {request.FrameRate} fps"));
            builder.AppendLine($"- Bones: {result.BoneCount}, region attachments: {result.RegionAttachmentCount}, mesh attachments: {result.MeshAttachmentCount}");
            builder.AppendLine($"- Animations: {string.Join(", ", skeleton.Animations.Select(animation => FormattableString.Invariant($"`{animation.Name}` ({animation.Duration:0.###} s)")))}");
            builder.AppendLine($"- Prefab: `{result.PrefabPath}`");
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            if (result.Notes.Count == 0)
                builder.AppendLine("- None.");
            foreach (string note in result.Notes)
                builder.AppendLine($"- {note}");

            return builder.ToString().Replace("\r\n", "\n");
        }

        internal static string RelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
                parts.Add(current.name);

            parts.Reverse();
            return string.Join("/", parts);
        }

        internal static string SafeName(string name)
        {
            return (name ?? "unnamed").Replace('/', '_').Replace('\\', '_').Trim();
        }

        private static string SafeFileName(string name)
        {
            string safe = SafeName(name);
            foreach (char invalid in Path.GetInvalidFileNameChars())
                safe = safe.Replace(invalid, '_');
            return safe;
        }

        private static string StripSpineExtensions(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(name) : name;
        }

        private static Color ToColor(SpineColor color) => new Color(color.R, color.G, color.B, color.A);
    }
}
