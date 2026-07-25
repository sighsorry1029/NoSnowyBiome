# Custom/Piece Snow-Only Replacement Plan

## Goal

For `noSnow: true` biomes, make `Custom/Piece` building materials render like their normal non-snow state.

This is not a "new no-snow style".
It should preserve the original building look and remove only the snow-like result that appears in the target biome.

## Current Findings

- `Custom/Piece` does not expose `_AddSnow`.
- Publicly visible piece-side weather inputs are `_AddRain` and `_Wet`.
- Runtime probe results showed that forcing `_AddRain = 0`, `_Wet = 0`, or both does **not** remove the snow-like visual on `wood_floor`.
- Therefore the visible effect is not controlled by the exposed `_AddRain` / `_Wet` path alone.

Implication:

- piece snow handling should not be approached as a simple material-property override problem
- piece snow handling should move to a `shader-family replacement` problem

## Important Constraint

Do **not** remove or alter rain/wet visuals as a feature goal.

Desired outcome:

- normal biome: unchanged
- `noSnow: true` biome: same building look, but without the snow-like overlay/result
- rain/wet behavior should remain unless it is proven to be inseparable from the snow-like branch

## Why The Previous Bundle Looked Wrong

The previous replacement shader in the Unity project was a simplified surface shader:

- [CustomPieceNoSnow.shader](C:/Users/blizz/Documents/vunity/ValheimDefault/Assets/NoSnowyBiome/Shaders/CustomPieceNoSnow.shader)

That shader copied many material values but did **not** preserve the real `Custom/Piece` rendering behavior closely enough.
The "burnt" / darkened look is consistent with a shader that diverges from the original lighting/noise/detail logic.

More concretely, the current replacement diverges in these important ways:

- it does not preserve the source material keyword set
- it does not implement the original `Custom/Piece` feature profile closely enough
- it reduces the shader to a much simpler Standard-surface path

For `woodwall`, that means at minimum the following source-side behavior is currently missing or underrepresented:

- `_PARALLAXMAP`
- `_VALUENOISEVERTEX_ON`
- `_Parallax`
- `_ValueNoise`
- `_ValueNoiseVertex`
- `_NoiseTex`
- `_RippleDistance`
- `_RippleFreq`
- original occlusion response
- original cutoff/cull/material-state behavior

## Strategy

Use `material family` as the working unit, not prefab name.

Example:

- `wood_floor`, `wood_wall`, and related structures share `woodwall` / `woodwall_worn`
- those should be solved by one family-level replacement strategy

## Preferred Technical Direction

### 1. Keep original material data

For each targeted material family:

- keep original textures
- keep original colors
- keep original floats
- keep original keyword intent as much as possible

The replacement should not invent new artistic values.

### 2. Replace shader behavior, not material identity

Target behavior:

- original `Custom/Piece` appearance
- snow-like branch removed or bypassed
- rain/wet behavior preserved if possible

This means the production solution should be:

- original-material-equivalent replacement materials
- backed by a `Custom/Piece`-compatible no-snow variant shader

### 3. Do not blanket-replace all piece families at once

First target:

- `woodwall`
- `woodwall_worn`
- optional lowlod variants

Only after visual parity is acceptable should other families be added.

## Implementation Phases

## Phase 1: Baseline Capture

For `wood_floor` / `woodwall` family, capture screenshots or repeatable observations in these states:

- normal biome, clear weather
- normal biome, rain if relevant
- target `noSnow: true` biome, same time/weather if possible
- target biome with the unwanted snow-like result

Success criterion for all later work:

- target biome result should match the normal biome baseline as closely as possible

## Phase 2: Family Audit

For the first family:

- confirm source materials:
  - [woodwall.mat](C:/Users/blizz/Documents/vunity/ValheimDefault/Assets/Material/woodwall.mat)
  - [woodwall_worn.mat](C:/Users/blizz/Documents/vunity/ValheimDefault/Assets/Material/woodwall_worn.mat)
- confirm shader reference:
  - [Custom_Piece.asset](C:/Users/blizz/Documents/vunity/ValheimDefault/Assets/Shader/Custom_Piece.asset)

Record the properties that must visually remain identical:

- `_MainTex`
- `_BumpMap`
- `_MetallicTex`
- `_NoiseTex`
- `_Color`
- `_EmissionColor`
- `_MetalColor`
- `_Glossiness`
- `_Metallic`
- `_MetallicAlphaGloss`
- `_Cutoff`
- `_RippleDistance`
- `_RippleFreq`
- `_ValueNoise`
- `_ValueNoiseVertex`
- `_TriplanarMap`
- `_TriplanarLocalPos`
- `_TriplanarScale`
- `_MoveableObject`
- `_Parallax`
- `_MossAlpha`
- `_MossNormal`
- `_MossTransition`

## Phase 3: Replace The Simplified Shader

The current simplified shader should be treated as a temporary diagnostic/fallback artifact, not the final direction.

Do one of the following:

1. Preferred: obtain a closer editable source representation of `Custom/Piece`
2. Fallback: hand-author a new shader that is materially much closer to `Custom/Piece` than the current simplified shader

Requirements for the new shader:

- same property surface as the source family needs
- original texture sampling and normal handling preserved
- original color/metal/smoothness response preserved as closely as possible
- no deliberate wet suppression
- only the snow-like branch/result removed

### Phase 3A: Compatibility Checklist For The First Improved Shader

Before trying to remove the snow-like branch, the replacement shader should first get much closer to `Custom/Piece` visual compatibility.

Checklist:

- keep the same exposed property names used by the source material
- preserve texture tiling and offset from the duplicated source material
- implement `_MainTex` sampling exactly once as the base albedo path
- preserve `_BumpMap` and `_BumpScale`
- preserve `_MetallicTex`, `_Metallic`, `_MetallicAlphaGloss`, `_MetalColor`
- preserve `_Glossiness`
- preserve `_Cutoff`
- preserve `_Cull`
- preserve `_EmissionMap` and `_EmissionColor`
- preserve `_OcclusionStrength`
- implement `_Parallax` if the source family uses `_PARALLAXMAP`
- implement `_NoiseTex`, `_RippleDistance`, `_RippleFreq`
- implement `_ValueNoise` and `_ValueNoiseVertex`
- carry through `_TriplanarMap`, `_TriplanarLocalPos`, `_TriplanarScale` even if `woodwall` does not currently rely on them
- carry through `_MoveableObject`
- preserve alpha/cutoff behavior for lowlod and broken-state renderers

### Phase 3B: Keyword Compatibility Checklist

The builder should stop discarding source-material keyword intent.

For the first `woodwall` pass, explicitly preserve or intentionally account for:

- `_NORMALMAP`
- `_PARALLAXMAP`
- `_VALUENOISEVERTEX_ON`

If the improved replacement shader does not implement one of these features yet, that is a known parity gap and should be treated as a blocker for color/look validation.

### Phase 3C: Snow-Branch Isolation Strategy

Do not start by guessing "the snow code".

Instead:

1. get the improved replacement to match the normal-biome `woodwall` look as closely as possible
2. only after parity is acceptable, identify and remove the extra bright/snow-like result in the target biome

Working rule:

- if a change also shifts the normal-biome look noticeably, it is probably not a snow-only fix
- if a change leaves the normal-biome look intact but removes the target-biome whitening, it is likely part of the snow-only path

## Phase 4: Build Family-Scoped Replacement Materials

Use the original material as the source of truth.

For each targeted material:

- duplicate the material
- keep its saved properties
- swap only the shader reference to the improved no-snow variant
- name using a stable suffix like `_nosnow`

Builder checklist:

- duplicate source textures without remapping them
- duplicate floats exactly
- duplicate colors exactly
- preserve render queue unless there is a proven reason not to
- preserve instancing setting
- preserve keyword state as closely as Unity serialization allows
- do not inject new artistic values
- keep the source material available for A/B comparison in the Unity project

This is better than inventing new values in code.

## Phase 5: Runtime Swap Scope

Runtime swap should happen only when all of these are true:

- biome has `noSnow: true`
- altitude threshold is met
- renderer material belongs to a targeted `Custom/Piece` family

Do not apply this to vegetation.
Vegetation should continue using `_AddSnow` suppression.

## Phase 6: Visual Validation

For `woodwall` family, validate:

- no more snow-like result in target biome
- no burnt/darkened look
- no obvious loss of normal/detail response
- no broken alpha/cutoff response
- no obvious regression under daylight
- no obvious regression under nighttime torch/fire lighting
- if rain is present, verify that wet/rain behavior still exists unless it is technically inseparable

Validation checklist for each shader iteration:

- compare `wood_floor` in a normal biome against the original unmodified material
- compare `wood_floor` in the target biome against the normal-biome baseline
- compare `Broken`, `Worn`, and lowlod renderer states separately
- compare daytime and nighttime
- compare close camera and mid-distance camera
- verify that parallax/noise response is still present on surfaces where it was visible before
- verify that the result is not merely "less white", but actually close to the original baseline tone

## Phase 7: Expand To More Families

Only after `woodwall` parity is acceptable:

- `woodpole*`
- `window_wood*`
- `logwall*`
- `DarkWood*`
- other families after validation

Do not bulk-enable all generated `_nosnow` materials again until the new shader path is validated on at least one family.

## Runtime Integration Rules

- keep piece replacement logic separate from vegetation suppression logic
- keep piece replacement behind biome/altitude rules
- avoid global piece weather overrides
- avoid `_AddRain` / `_Wet` suppression as a production solution

## Short-Term Action Items

1. Retain the current debug probe only for diagnosis
2. Stop treating `_AddRain`/`_Wet` as the main production path
3. Build a new `woodwall`-only replacement shader/material pass that first aims for normal-biome parity
4. Restore missing feature coverage in the shader before trying to isolate the snow-only branch
5. Compare against normal-biome baseline
6. Expand only after parity is proven

## Practical Implementation Order

If continuing in Unity, the most defensible order is:

1. Keep `woodwall` and `woodwall_worn` as the only target family
2. Replace the current simplified shader with a more compatible `Custom/Piece`-style shader
3. Add back feature parity in this order:
   - normal map
   - metallic/smoothness
   - occlusion
   - parallax
   - noise/value-noise
4. Make the duplicated `_nosnow` materials preserve the source keyword set
5. Verify that the replacement looks close to the original in a normal biome
6. Only then start identifying which branch or calculation causes the target-biome snow-like result
7. After `woodwall` is acceptable, repeat per material family

## Decision Rule

If the improved replacement still cannot match the normal-biome look closely enough, the remaining path is deeper shader reverse-engineering rather than more material-value tweaking.
