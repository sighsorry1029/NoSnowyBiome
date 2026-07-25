# NoSnowyBiome

In Valheim, snowy visuals on pieces and vegetation are hardcoded.
This mod removes the snow accumulation visual effect from vegetation-like world objects in selected biomes, mainly for custom biomes added by Expand World Data.

The active build handles vegetation-oriented world object snow visuals.

Expand World Data is the intended setup because this mod reads custom biome rules from `expand_biomes.yaml`. <br>

![](https://i.ibb.co/Qvy3Psrk/1.png) <br>
Snowy visuals of vegetations and pieces over altitude > 50 <br>

![](https://i.ibb.co/VcMcVnG3/2.png) <br>
After installing this mod and configuring to not have snowy visuals <br>
(Can't remove snowy visuals on pieces for now unfortunately) <br>

## What It Reads

This mod supports extra fields inside Expand World Data's `expand_biomes.yaml` biome entries:

- `noSnow`
- `noSnowMinAltitude`

These are not official Expand World Data fields. `NoSnowyBiome` reads them from the same synced YAML and applies its own behavior.

## `expand_biomes.yaml` Example

```yaml
- biome: MyCustomBiome
  terrain: Plains
  nature: Plains
  noSnow: true
  noSnowMinAltitude: 50
```

Meaning:

- `noSnow: true`
  Enables snow visual removal for supported vegetation-like objects in that biome.
- `noSnowMinAltitude: 50`
  Starts removing supported object snow visuals at altitude `50` and above.

## Behavior Priority

For world object snow removal, priority is:

1. `expand_biomes.yaml` rule for that biome

If a biome does not have a `noSnow` YAML rule, the active build leaves that biome unchanged.

If `noSnowMinAltitude` is missing but `noSnow: true` exists, the active build uses the internal default altitude `50`.

## Minimap Colors

NoSnowyBiome 1.1.0 no longer patches minimap colors. Expand World Data 1.68 and later provide their own minimap color controls, so configure minimap behavior through Expand World Data instead. Legacy `noSnowWhitening` fields are not supported.

## Installation

- The server does not need `NoSnowyBiome.dll`, but fields in the server's `expand_biomes.yaml` can be synced to clients. <br>
- Clients need `NoSnowyBiome.dll` for the `noSnow` fields to take effect. They do not need duplicate local entries when the server syncs them.

## Supported Active Object Types

The active build attaches suppression to vegetation-oriented runtime objects such as:

- `StaticPhysics`
- `Pickable`
- `PickableItem`
- `Plant`
- `TreeBase`
- `TreeLog`
- `Destructible`
- `MineRock5`

## How Vegetation Suppression Works

The vegetation patch is simple:

1. When a supported vegetation-like object wakes up, the mod checks its biome and altitude.
2. If that biome has `noSnow: true` and the object is above the configured minimum altitude, the mod forces the material snow value `_AddSnow` to `0`.
3. If the shader uses the `_ADDSNOW_ON` keyword, that snow path is disabled as well.

In practice, this means the mod is not repainting the whole world every frame. It attaches to supported vegetation-style objects when they load, applies the no-snow material state, and then stops.

## Why Pieces Are Not Patched In The Active Build

Vegetation and pieces do not expose the same controls.

- Vegetation shaders expose `_AddSnow`, so the active mod can disable snow accumulation directly and cleanly.
- Many build pieces use `Custom/Piece`, and that shader path does not expose a matching `_AddSnow` control in the same way.

That made piece handling much less reliable:

- direct suppress paths did not consistently remove the visual
- replacement-material experiments changed the look of the piece and could drift away from the original material appearance
