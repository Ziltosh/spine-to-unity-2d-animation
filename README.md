# Spine to Unity 2D Animation

Editor-only Unity package that converts **Spine 3.8 JSON exports** into **native Unity 2D Animation** assets, so a project can drop the Spine runtime and keep its existing animations.

From a skeleton JSON, its atlas and the atlas pages, the converter produces:

- a prefab with the bone hierarchy, one `SpriteRenderer` per attachment and a `SortingGroup`;
- one sprite per atlas region, rebuilt in its original size and orientation (packing rotation, whitespace stripping and premultiplied alpha are undone);
- weighted and unweighted meshes as skinned sprites (mesh, bones and weights stored with 2D Animation, bound by `SpriteSkin`);
- one looping `AnimationClip` per Spine animation and an `AnimatorController`;
- a `conversion-report.md` listing everything that was approximated or skipped.

No code from the Spine runtimes is used: the package only reads the documented JSON and atlas text formats. You still need the rights to the art you convert.

## Requirements

- Unity 6000.6 or newer
- `com.unity.2d.animation` 16.0.1 (installed as a dependency)

## Installation

Add the package with Unity Package Manager, from a local folder (`file:../path/to/spine-to-unity-2d-animation`) or from its Git URL.

## Usage

Menu: **Tools > Spine to 2D Animation > Convert Spine Export...**, then pick the JSON, the atlas and an output folder inside `Assets/`.

Batch mode:

```bash
Unity -batchmode -quit -projectPath <project> \
  -executeMethod SpineTo2DAnimation.Editor.SpineConverterBatch.Convert \
  -spineJson <skeleton.json> -spineAtlas <skeleton.atlas.txt> \
  -output Assets/Characters/Hero [-name Hero] [-ppu 100] [-fps 30]
```

Code:

```csharp
SpineConversionResult result = SpineConverter.Convert(new SpineConversionRequest
{
    SkeletonJsonPath = jsonPath,
    AtlasPath = atlasPath,
    OutputFolder = "Assets/Characters/Hero"
});
```

Reconverting into the same folder rewrites the assets in place: GUIDs are kept, so scenes referencing the prefab or clips stay valid, and files no longer produced are removed. Two runs on the same export produce identical files.

## Supported Spine features (v0.1)

| Feature | Status |
|---------|--------|
| Bones, "normal" transform inheritance | ✅ |
| Slots, draw order, slot and attachment colors | ✅ (setup draw order) |
| Region attachments | ✅ |
| Mesh and linked mesh attachments, weighted or not | ✅ (4 strongest influences per vertex) |
| Bone rotate / translate / scale timelines, Bezier and stepped curves | ✅ (sampled, 30 fps by default) |
| Slot attachment and color timelines | ✅ |
| Default skin | ✅ (other skins reported) |
| IK constraints (one or two bones) | ✅ solved and baked into bone rotations |
| Transform and path constraints, IK timelines, IK compress/stretch/softness | ⚠️ reported, not converted |
| Deform, draw order and event timelines | ⚠️ reported, not converted |
| Shear, non-normal inheritance modes, blend modes, clipping, bounding boxes, points, paths | ⚠️ reported, not converted |

Meshes are placed through a similarity fit between their image layout and their setup pose. The fit residual is reported; a residual above 1 px means the mesh was deformed in its setup pose and is approximated. 2D Animation bind poses carry no scale, so bones with a non-unit setup scale are reported too.

## Tests

The EditMode tests build their own synthetic Spine export; add the package to `testables` in your project manifest to run them.

## License

MIT, see [LICENSE.md](LICENSE.md). Spine is a trademark of Esoteric Software; this project is not affiliated with it.
