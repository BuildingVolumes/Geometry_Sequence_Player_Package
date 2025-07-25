---
title: "Changelog"
description: "All releases"
lead: "All releases"
date: 2020-11-16T13:59:39+01:00
lastmod: 2020-11-16T13:59:39+01:00
draft: false
images: []
menu:
  docs:
    parent: "about"
weight: 420
toc: true
---

### Version 1.1.1

Fixed Issues:

- Removed unnessesary, build breaking using reference from samples script  

### Version 1.1.0

Added features:

- Huge overhaul of the playback system. Performance is enhanced by up to 10x-20x.
- Pointcloud geometry shaders have been replaced by a compute shader system
- Mac, Iphone, Ipad and VisionOS (Assets store version only) platforms are now supported
- Compressed .astc textures for mobile devices are now supported
- Converter Tool supports reduction of pointcloud sizes
- More reliable playback. Lags, or low framerates don't affect the playback speed anymore
- Added frame debugging/performance tools
- URP and HDRP render pipelines are now supported
- Unity 6000 is now supported

Fixed Issues:

[#7 "visionOS Support"](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/issues/7)

[#5 "Pointcloud shader not working on Metal/OpenGL](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/issues/5)

### Version 1.0.3

Added features:

- Thumbnails will now show when adding a sequence via the editor
- Looping is now much smoother and should not lag anymore
- Added Playback Events that can be used to see when a scene has been loaded, started playing ect.
- Added more refined media loading via script API
- Added Stop functionality to the player

### Version 1.0.2

Added features:

- A parent transform for the streamed mesh can be set
- Pointcloud meshes are now mirrorable
- Added better documentation in some parts
- Added Unity Test Project to the main repository

Fixed Issues:

[#6 "The transform of Streamed Meshes should be settable before going into play mode"](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/issues/6)

[#3 "Meshes/Pointclouds are mirrored on the X-Axis"](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/issues/3)

Releases:
[Package release v1.0.2](https://github.com/BuildingVolumes/Geometry_Sequence_Player_Package/releases/tag/v1.0.2)

### Version 1.0.1 (Package only)

Fixed Issues:

[#1 "Cannot build as AssetDatabase cannot be used outside the editor"](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/issues/1)

[#2 "Android /WebGL cannot load data from StreamingAssets Path"](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/issues/2)

Releases:
[Package release v1.0.1](https://github.com/BuildingVolumes/Geometry_Sequence_Player_Package/releases/tag/v1.0.1)

### Version 1.0.0

Initial Release, this plugin supersedes the [Pointcloud Player Package](https://github.com/ExperimentalSurgery/Unity_Geometry_Sequence_Player)

[Converter release v1.0.0](https://github.com/BuildingVolumes/Unity_Geometry_Sequence_Player/releases/tag/v1.0.0)
[Package release v1.0.1](https://github.com/BuildingVolumes/Geometry_Sequence_Player_Package/releases/tag/v1.0.0)
