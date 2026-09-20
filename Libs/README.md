# Vendored Dependencies

`ServerSync.dll` is the reviewed `valheim-1.0.7-r1` baseline, vendored on 2026-09-20. It replaces the unmodified older baseline previously included in this project.

- Upstream: https://github.com/blaxxun-boop/ServerSync/tree/c57c2aa54e07cdcc7630d6068699ea781622323e
- SHA-256: `b4dd786997f4e90d770f09ef3e9d64154754fe7e8edfb4841795751895b35846`
- Assembly version: `1.0.0.0`; file version: `1.0.0.1`.
- Original local input SHA-256: `166956302a294e224474b26f4c7d58409084ad3f48bd0af1feb7551f229c8f60`.
- Changes: compile against original Valheim APIs (including the constant `ZRoutedRpc.Everybody`), use public `ZNet.IsAdmin`, preserve initial player/admin/time message ordering during config synchronization.
- Reproducible source and evidence: `C:/Users/blizz/.codex/references/valheim/integrations/serversync/versions/valheim-1.0.7-r1/`.
- License: MIT-0, preserved in `ServerSync.LICENSE.txt`.

The project compiles against this pinned file and internalizes it into the final NoSnowyBiome DLL. Builds do not fetch or replace it from the global reference folder. It is not a standalone BepInEx plugin.

YamlDotNet 16.3.0 (MIT) is pinned through NuGet and internalized by the same ILRepack target. Package assets and licensing remain available in the NuGet package.

The common ServerSync baseline's original 1.0.7 checks do not establish this mod's 1.0.15 game/network runtime compatibility. See the project tests and development verification scope.
