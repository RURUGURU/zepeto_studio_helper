# Environment Setup

This helper assumes a normal ZEPETO Studio Unity project and adds only the workflow helper window.

## Tested Versions

| Item | Version / Value |
| --- | --- |
| OS | Windows 11 |
| Unity | `2020.3.9f1` |
| ZEPETO Studio | `3.2.12` |
| Helper | `com.easy.zepeto-helper@0.2.0` |
| ZEPETO registry | `https://upm.zepeto.run` |

## Required Packages

Minimum required ZEPETO dependency:

```json
"zepeto.studio": "3.2.12"
```

The project used for verification also had Unity's default editor/UI packages such as:

```json
"com.unity.textmeshpro": "3.0.9",
"com.unity.ugui": "1.0.0",
"com.unity.test-framework": "1.1.33"
```

## Scoped Registry

Add the ZEPETO scoped registry to `Packages/manifest.json` if it is not already present:

```json
{
  "name": "ZEPETO",
  "url": "https://upm.zepeto.run",
  "scopes": [
    "zepeto"
  ]
}
```

## Add This Helper

Git URL install:

```text
https://github.com/RURUGURU/zepeto_studio_helper.git
```

Local development install:

```json
"com.easy.zepeto-helper": "file:com.easy.zepeto-helper"
```

## Scene Requirements

Before opening the helper, make sure the project has:

- a ZEPETO `LOADER` object in the active scene
- a valid ZEPETO avatar ID
- a clothing prefab under `Assets/Contents`
- the official ZEPETO Studio export menu available at `Assets/Zepeto Studio/Export as .zepeto`

## What Is Not Included

This helper does not include:

- ZEPETO Studio SDK itself
- ZEPETO account login
- upload/review permissions
- Maya/Blender mesh or rig editing
- automatic cloth simulation fixes

It is a Unity Editor workflow helper around the official SDK.
