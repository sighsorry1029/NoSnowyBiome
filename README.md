# NoSnowyBiome

Controls altitude-based snow visuals on vegetation, world objects, and supported building materials in selected Valheim biomes. Works standalone; Expand World Data (EWD) is optional and enables rules targeting its custom biome names.

Targets Valheim 1.0.15. It does not generate biomes, modify terrain, change minimap colors, or replace shaders.

![](https://i.ibb.co/Qvy3Psrk/1.png)

![](https://i.ibb.co/VcMcVnG3/2.png)

## Installation and Server Sync

Requires BepInExPack Valheim 5.4.2351. Install `NoSnowyBiome.dll` in `BepInEx/plugins`.

- **Standalone / client only:** uses the client's own rules. EWD and a server installation of NoSnowyBiome are not required.
- **Server-controlled rules:** install this build on the server/host and clients. The server sends its rules and `General.Enabled` setting to clients, including subsequent YAML edits. A dedicated server only handles configuration, not rendering.
- **Optional installation:** clients without NoSnowyBiome are not rejected, but will retain vanilla visuals. A client with the mod can still join a server without it and use local rules.
- **Local settings are preserved:** received YAML is used in memory, never written over the client's YAML. Local edits made while connected are retained for after disconnect and do not replace the active server rules. ServerSync restores local settings on disconnect/world shutdown. Synchronized configuration uses ServerSync's normal non-admin lock and administrator permissions.
- **EWD worlds:** EWD is still needed wherever the world requires it. NoSnowyBiome's optional integration does not replace EWD or its own client/server requirements.

ServerSync and YamlDotNet are bundled inside the mod DLL; do not install them as separate plugins.

## Rules

On first launch the mod creates `BepInEx/config/NoSnowyBiome.yaml` with snow suppression enabled for Mistlands at altitude 50 and above. Existing files are not overwritten. The example below also includes an optional custom-biome rule.

```yaml
- biome: Mistlands
  noSnow: true
  noSnowMinAltitude: 50

- biome: MyCustomBiome
  noSnow: true
  noSnowMinAltitude: 0
```

- `biome` is one vanilla biome name or an EWD `biome` identifier, not its translated display name, a numeric ID, or a biome mask. Names are matched case-insensitively.
- `noSnow: true` suppresses supported snow visuals. Omitted or `false` leaves the biome unchanged.
- `noSnowMinAltitude` is height above the current water level, measured at the object's origin. The threshold is inclusive and defaults to `50` when omitted.
- Unlisted biomes remain unchanged. Use `[]` to clear all rules and restore original materials.
- File size, modification time, and creation time are checked once per second; the contents are read only when this metadata changes or a failed read needs retrying. Save normally so the file timestamp is updated. Malformed YAML, unknown fields, duplicate rules, invalid numbers, or a temporarily missing file retain the last valid rules. Files must be at most 1 MiB.

`General.Enabled` and local-only `Debug.VerboseLogging` are in `BepInEx/config/sighsorry.NoSnowyBiome.cfg`. Configuration-manager changes apply live; restart after editing the CFG by hand. The YAML rules reload automatically.

## EWD Integration and Existing Configurations

EWD is a soft dependency. When present, NoSnowyBiome queries its public biome-name lookup and re-resolves rules after delayed synchronization or name/ID changes. Unresolved names remain inactive until EWD knows them.

The optional adapter targets EWD 1.79's `ExpandWorldData.Biome.BiomeManager.TryGetBiome` API. The older manager namespace is not supported. EWD's DLL and YAML files are not modified.

**The old fields inside `expand_biomes.yaml` are no longer read.** Move `biome`, `noSnow`, and `noSnowMinAltitude` rules into `NoSnowyBiome.yaml`, then remove `noSnow` and `noSnowMinAltitude` from the EWD file. Keep terrain, nature, environments, and other EWD settings in EWD's file. Existing files are not rewritten automatically and there is no legacy-format fallback.

Leaving the old custom fields in EWD's YAML can still produce EWD's unknown-property errors. NoSnowyBiome no longer intercepts EWD's YAML parsing or suppresses its logging.

## Visual Scope

Supported runtime objects include `StaticPhysics`, `Pickable`, `PickableItem`, `Plant`, `TreeBase`, `TreeLog`, `Destructible`, `MineRock5`, and `Piece` objects with supported materials.

The mod uses each shader's existing `_AddSnow` property: float for vegetation and integer for the current `Custom/Piece` shader. It also disables `_ADDSNOW_ON` where used. No custom shader bundle is needed for these surface effects.

Suppressed renderers sharing the same original material reuse one snow-disabled variant, including inactive wear/LOD children. The original material is never modified. Each slot retains its original reference; the shared variant is released when its last user restores or unloads. Slots replaced by another system are preserved, and unrelated material property blocks are not overwritten.

Loaded objects are re-evaluated after rule changes, biome-name changes, and movement. If the final suppression state is unchanged, the mod does not recreate or reassign materials. A rolling refresh pass starts roughly once per second (or earlier for new objects/settings) and is spread across frames: at most 64 objects per frame, with a roughly 1 ms time budget checked between objects. One expensive object can exceed that time budget; crowded scenes can take longer than a second to finish a pass. Biome-name resolution reuses its dictionary buffers while still detecting EWD ID changes.

This does **not** remove Deep North's separate accumulated snow meshes/`_SnowLevel` system, change weather/freezing/gameplay, or add controls to shaders that have no supported snow property. Materials overridden by another mod's `_AddSnow` property block can take precedence. Minimap colors remain EWD's responsibility when using EWD.
