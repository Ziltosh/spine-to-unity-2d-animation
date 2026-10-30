# Changelog

## 0.2.1 - 2026-10-30

- Added: repository, documentation and changelog links in the package manifest.
- Changed: rotate timelines wrap their delta with `Mathf.Repeat`, with the same shortest-path result.

## 0.2.0 - 2026-10-24

- Added: one- and two-bone IK constraints solved like the Spine 3.8 runtime and baked into bone rotations (setup pose and animations).
- Changed: reconverting an export rewrites assets in place (stable GUIDs, deterministic bone ids, untouched controller) and removes stale files; two runs produce identical files.
- Changed: baked curves are simplified within a small tolerance (0.05°, 0.05 px, 0.2 % color), roughly six times smaller clips.
- Changed: generated sprites use compressed textures.

## 0.1.0 - 2026-10-24

- Added: Spine 3.8 JSON and libGDX atlas readers (3.7 skin objects accepted).
- Added: prefab generation with bone hierarchy, region sprites and skinned meshes bound by SpriteSkin.
- Added: animation baking for bone rotate/translate/scale and slot attachment/color timelines.
- Added: conversion report listing approximations and unsupported features.
- Added: batch and menu entry points, EditMode tests on a synthetic export.
