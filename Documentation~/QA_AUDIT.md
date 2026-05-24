# QA Audit Notes

## Scope

Package: `com.easy.zepeto-helper`

Primary file: `Editor/ZepetoStudioHelperWindow.cs`

Goal: make the current helper usable by other people as a Unity package, with legacy code removed and high-risk behavior documented.

## Audit Findings

- Major: Editing package/cache animation clips directly would corrupt SDK assets. The current flow copies clips into `Assets/ZepetoHelper/Animations` before saving clip timing changes.
- Major: Preview Play must not replace the user's working clip permanently. The current flow assigns `clip_adjust_preview.anim` only for preview and restores the original clip after Play exits.
- Major: ZEPETO export writes the official `<outfit>.zepeto` first. The helper renames that file only after the official output exists.
- Major: `SerializedObject` references can become stale after export or domain reload. The helper refinds LOADER fields instead of dereferencing destroyed targets.

## QA/QC Status

- Unity script compile: Pass
- Console check for `ZepetoStudioHelperWindow.cs`: Pass
- Console check for `error CS`: Pass
- Legacy UI string scan for `Motion Adjust` / `PoseEdit` / `MotionEdit`: Pass
- Functional smoke export: Pass
- Manual UI walkthrough by another user: Not run

## Verification Commands

Run from the Unity project root:

```powershell
rg -n "PoseEdit|MotionEdit|Motion Adjust|모션 조정" Packages\com.easy.zepeto-helper\Editor\ZepetoStudioHelperWindow.cs
```

Expected: no matches.

Use Unity MCP or the Unity Editor:

```text
Assets > Refresh
Console filter: ZepetoStudioHelperWindow.cs
Console filter: error CS
```

Expected: zero C# errors.

Smoke export evidence from this project:

```text
ZepetoHelperExportSmoke PASS: Assets/Contents/TRANSPARENT_1/ZEPETO_TRANSPARENT_1_VideoBooth_139_v02.zepeto
```

## Residual Risks

- ZEPETO SDK export behavior is external to this helper. If the SDK changes its output filename, `MoveOfficialExportToFriendlyName` may need updating.
- The package targets the observed ZEPETO Studio `3.2.12` workflow. Other SDK versions need separate verification.
- Full user acceptance still needs a manual pass through all four UI steps on a clean project.
