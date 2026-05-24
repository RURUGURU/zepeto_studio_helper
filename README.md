# Easy ZEPETO Studio Helper

Beginner-friendly Unity Editor helper for checking ZEPETO clothing prefabs, selecting motions, saving simple clip timing edits, and creating readable `.zepeto` export files.

![Easy ZEPETO Studio Helper workflow](docs/images/workflow.png)

## What This Solves

ZEPETO Studio export already works, but the normal workflow is easy to confuse:

- whether the avatar ID and outfit are actually connected
- whether Play is previewing the selected motion or every step at once
- whether a clip edit changed the real asset or only a preview
- where the final `.zepeto` file was saved

This helper turns that into a locked 4-step flow:

1. `Avatar & Outfit` - apply ID, select outfit, confirm in Play.
2. `Motion Select` - choose a ZEPETO motion, preview, then apply as the working motion.
3. `Clip Adjust` - adjust speed, start/end time, and loop, then save a new `.anim` copy.
4. `Save & Export` - confirm the final result and create a readable `.zepeto` file.

## Quick Answer

Yes, this is designed to work with the official Unity/ZEPETO setup plus this helper package.

You still need the official ZEPETO Studio SDK and its normal project setup. This helper does not replace the ZEPETO SDK, account login, upload flow, or the official exporter. It wraps the confusing parts inside Unity and calls the official export menu.

## Tested Environment

- Windows 11
- Unity `2020.3.9f1`
- ZEPETO Studio package `3.2.12`
- ZEPETO scoped registry: `https://upm.zepeto.run`
- Helper package: `com.easy.zepeto-helper@0.2.0`

See [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md) for the full setup checklist.

## Install From GitHub

In Unity:

1. Open `Window > Package Manager`.
2. Press `+`.
3. Choose `Add package from git URL...`.
4. Enter:

```text
https://github.com/RURUGURU/zepeto_studio_helper.git
```

Then open:

```text
Window > Easy > ZEPETO Studio Helper
```

## Install From Local Tarball

This repository can also be packed as a Unity Package Manager tarball:

```powershell
npm pack
```

The verified local build artifact was:

```text
Build/Packages/com.easy.zepeto-helper-0.2.0.tgz
```

In Unity Package Manager, choose `Add package from tarball...` and select the `.tgz`.

## Required Unity Project Setup

Your Unity project must already have:

- official ZEPETO Studio SDK installed
- `zepeto.studio` available through the ZEPETO scoped registry
- a scene with a ZEPETO `LOADER`
- a clothing prefab under `Assets/Contents`
- normal ZEPETO account/export permissions if you plan to upload after export

Minimal `Packages/manifest.json` scoped registry:

```json
{
  "scopedRegistries": [
    {
      "name": "ZEPETO",
      "url": "https://upm.zepeto.run",
      "scopes": ["zepeto"]
    }
  ],
  "dependencies": {
    "zepeto.studio": "3.2.12"
  }
}
```

## Output Paths

- Working animation copies: `Assets/ZepetoHelper/Animations`
- Saved clip edits: `Assets/ZepetoHelper/Animations/ClipEdits`
- Temporary Play preview clip: `Assets/ZepetoHelper/Animations/Preview/clip_adjust_preview.anim`
- Final export: beside the outfit prefab, named like:

```text
ZEPETO_<OutfitName>_<MotionName>.zepeto
```

Example verified output:

```text
Assets/Contents/TRANSPARENT_1/ZEPETO_TRANSPARENT_1_VideoBooth_139_v02.zepeto
```

## Safety Model

- Package/cache animation clips are never edited in place.
- Clip timing changes are saved as new `.anim` files.
- Play preview uses temporary assets and restores the working clip after Stop.
- Export only runs when Unity Play Mode is stopped.
- The UI displays the expected `.zepeto` output path.

## QA Status

- Unity compile check: Pass
- `ZepetoStudioHelperWindow.cs` console error check: Pass
- `error CS` console check: Pass
- Legacy motion/pose edit scan: Pass
- Export smoke test: Pass

Details: [Documentation~/QA_AUDIT.md](Documentation~/QA_AUDIT.md)
