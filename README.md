# Leopard 2A7V for GHPC

A MelonLoader mod that replaces the in-game Leopard 2A4-style hull/turret appearance with a
Leopard 2A7V vehicle model, re-rigged onto the donor unit's skeleton, plus its own ammunition,
gun elevation limits, roof-mounted machine gun and daylight reticle.

Runs on **GHPC** (Radian Simulations) with **MelonLoader** and the base game's
`LEO2A3` unit present (that is the donor the mod builds on).

## What is not in this repository

The vehicle model data — `Leopard2A7V.obj`, `Leopard2A7V.mtl` and the `textures/` folder — is
**not committed**. The tracked source only contains the C# that loads, converts and drives it.
To run the mod you must supply your own converted model set in the expected format:

```
<game>/Bin/Mods/Leopard2A7V/
    Leopard2A7V.obj          # single OBJ, skinned weights optional
    Leopard2A7V.mtl          # one material per texture channel
    textures/                # DDS maps referenced by the MTL
```

`ObjModel.Preload` reads exactly those filenames, so a folder without them makes the mod log
`asset folder missing` and skip loading.

## Build

```
dotnet build -c Release -p:GameDir="D:/Steam/steamapps/common/Gunner HEAT PC/Bin"
```

`GameDir` points at the game's `Bin` folder; the project resolves `Assembly-CSharp`,
MelonLoader, Harmony and the Unity modules from there. Without `-p:GameDir=` it falls back to
the path baked into `Leopard2A7V.csproj`. Output: `bin/Release/Leopard2A7V.dll`.

## Install

1. Copy `bin/Release/Leopard2A7V.dll` to `<game>/Bin/Mods/Leopard2A7V.dll`.
2. Put the model data described above into `<game>/Bin/Mods/Leopard2A7V/`.
3. Start the game; the mod registers the unit on every scene load and prints `[L2A7V]` lines
   in `Bin/MelonLoader/Logs/Latest.log`.

## Ammunition

The 2A7V's own APFSDS round is built at runtime as a copy of the donor's AP shell with the
2A7V's ballistics, so it is not a game asset. The mod clones the mission customizer's 120 mm AP
logistics set and puts its own round first, which means:

- the round is selectable **before** the mission, in the flex-mission ammunition dropdown;
- leaving the dropdown untouched loads the 2A7V's shell instead of the donor's default;
- the donor's original options stay listed after it, and HEAT/HE and the in-mission rack
  behaviour are unchanged.

Campaign sorties ignore mission-customizer overrides, so the round is not offered there yet.

## Configuration

`MelonLoader/Leopard2A7V.cfg` (created on first run). Everything is optional; the defaults are
what the mod was tuned with.

| Entry | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `true` | master switch |
| `SourceUnitKey` | `LEO2A3` | donor unit cloned and re-skinned |
| `FriendlyName` | `Leopard 2A7V` | display name |
| `ConvertSourceUnit` | `false` | also convert the donor unit itself |
| `UseDonorRunningGear` | `false` | keep donor road wheels instead of the model's |
| `UseDonorTrackArt` | `true` | roll the donor's skinned track mesh |
| `TrackTreadsPerMetre` | `0.746` | track scroll speed for the donor's tread pitch |
| `ApplyArmor` / `LogArmor` | `false` / `true` | write model armor layers, or only report them |
| `VisualOffset` | `0,0,0.336` | model-space correction between prefab and unit frames |
| `ModelTurretAxis` | `0,1.620,0.403` | turret trunnion in model space |
| `ModelGunAxis` | `0.003,1.969,1.369` | gun elevation axis in model space |
| `CommanderViewOffset` | `0,0,0` | nudge the commander's external camera |
| `MaxTextureSize` | `2048` | downscale imported maps |
| `AlbedoLuminance` | `0` | shift albedo toward grey (`0` = keep) |
| `PaintMaskLevel` / `SmoothnessLevel` | `255` / `63` | gloss/mask channel handling |
| `NeutralMaps` | `true` | synthesize missing normal/detail maps |
| `UseDetailMaps` | `true` | bind the model's detail layer |
| `AddHeatSource` | `false` | give the vehicle a heat signature source |
| `ReplaceDayReticle` | `true` | draw the 2A7V graticule in the daylight gunner sight |
| `LimitMainGunElevation` | `true` | apply the arc below |
| `GunElevationDegrees` / `GunDepressionDegrees` | `20` / `-9` | main gun travel |
| `AddRoofGun` | `true` | make the roof MG a usable weapon |
| `RoofGunYawPoint` / `RoofGunTrunnion` / `RoofGunMuzzle` | see cfg | roof MG geometry in model space |
| `RoofGunSight` / `RoofGunSightFov` | `-0.90,2.92,-0.60` / `20` | roof MG sight camera |
| `DumpMaterials` | `true` | log the model's material list at load |

## Layout

| File | Role |
| --- | --- |
| `Main.cs` | Melon entry point, preference creation, asset preload, patch install |
| `LpConfig.cs` | every tunable, with defaults |
| `LpPatches.cs` | Harmony patch registration |
| `LpTemplate.cs`, `LpUnitRegistry.cs` | clone the donor unit into a new prefab and register it |
| `ObjModel.cs`, `LpAssets.cs` | OBJ/MTL/DDS parser and runtime material/texture building |
| `LpVisual.cs`, `LpBaseRig.cs` | attach the art to the donor skeleton, hide donor art |
| `LpRunningGear.cs`, `LpTracks.cs` | road-wheel / torsion-bar animation and track scrolling |
| `LpSightView.cs`, `LpReticle.cs` | sight camera slots, magnification steps, graticule drawing |
| `LpWeapons.cs`, `LpAmmoMenu.cs`, `LpRoofGun.cs` | ammunition, pre-mission ammo row, roof MG |
| `LpArmorLog.cs`, `LpDiagnostics.cs`, `LpDebug.cs` | offline verification and console helpers |

## Status

Actively developed; the model conversion, rig attachment and ammunition paths are working, while
some geometry alignment (mantlet offset, suspension seating) is still being measured against the
log output. Expect tuning to move.
