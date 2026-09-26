# Unity client (M4) — setup

The client logic lives in `rewrite/src/ClientCore` (connection flow, zone state, interpolation,
local movement), tested without Unity. The Unity project only draws: three scripts in
`unity/Assets/EQClassic/Scripts` on top of LanternUnityTools, which imports the zones and models.

## Requirements

- Unity Hub and **Unity 2021.3.18f1**. LanternUnityTools needs this version and URP 12.1.10; newer
  versions fail to compile its EQ shaders. It needs a Unity account (Personal licence is enough).
- The Trilogy client files, for example `~/eq-client`.
- The .NET SDK, `rsync` and **`git-lfs`** (LanternUnityTools stores its DLLs in Git LFS; without
  it `Lantern.EQ` fails with "Melanchall could not be found"). On Linux, `libgdiplus` for texture
  conversion.

## Steps

```sh
# 1. Export the zones you want (characters come with each zone's _chr file)
tools/lantern/extract.sh ~/eq-client qeynos2 grobb permafrost

# 2. Assemble the Unity project in build/unity-client (LanternUnityTools + our scripts and DLLs + exports)
tools/unity/setup-client.sh

# 3. Start the servers (login, world and zones), with the collision meshes
cd rewrite && dotnet run --project src/Server -- \
  --db "Server=127.0.0.1;User ID=eqc;Password=eqc;Database=eqclassic;SslMode=None" \
  --lantern ../build/lantern-work/Exports
```

4. In Unity Hub, open `build/unity-client`. The first import takes several minutes.
5. Run **EQClassic > Import Zones and Characters** (zones from `EQC_ZONES`, default `qeynos2`), or
   LanternUnityTools' own **EQ > Assets > Import Zone** and **Import Characters**. Our menu runs the
   same importers without their dialogs and under the invariant culture: on a French system theirs
   fail with a FormatException (they parse `0.5` with the current culture).
6. Open an empty scene and press **Play**. The login screen appears. Enter the server address, an
   account (for example `bot` / `bot`), pick the world and a character. You then walk with
   WASD or the arrows and turn with Q/E. The NPCs follow their paths as the server moves them.

`setup-client.sh` can be run again at any time, for example after changing the scripts or the
libraries. It never touches Unity's `Library/` cache or the imported assets
(`Assets/Content/AssetBundleContent`).

Scripted sessions (what the test below used), from the repository root:

```sh
Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBatch.ImportAll -quit
EQC_HOST=127.0.0.1 EQC_PORT=5999 EQC_FINGERPRINT=<printed by the server> EQC_USER=bot \
  Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBatch.Play
```

`-batchmode` does not work with a Personal (entitlement) licence ("Access token is unavailable"):
run the editor normally, on a virtual display if needed (`Xvfb`).

## Unity 2021.3 on a recent Linux (Ubuntu 26.04)

The editor predates the distribution; three things are missing or broken, all fixed locally
without touching the system:

| Symptom | Cause | Fix |
|---|---|---|
| `libxml2.so.2: cannot open shared object file` at start-up | Ubuntu 26.04 ships libxml2.so.16 and ICU 76+ | Extract `libxml2.so.2*` and `libicu*.so.74*` from the Ubuntu 24.04 (noble) packages into a folder, start Unity with `LD_LIBRARY_PATH=<folder>` |
| Script compilation fails, csc exit code 134, "No usable version of libssl was found" (in `Library/Bee/tundra.log.json`) | Unity's bundled .NET 5 only loads OpenSSL 1.x | Add `libssl.so.1.1` and `libcrypto.so.1.1` from the Ubuntu 20.04 (focal) `libssl1.1` package to the same folder |
| The editor hangs forever on "compiling scripts", `bee_backend` idle | `bee_backend --stdin-canary` finishes the build but never exits on this glibc/kernel | Rename `Editor/Data/bee_backend` to `bee_backend.real` and put a wrapper script in its place that runs it without `--stdin-canary` |

A very high open-file limit (`ulimit -n` 524288) is also better lowered to 4096 before starting
the editor.

## What exists and what does not yet

| Works (tested) | Not yet |
|---|---|
| Login, world and zone connection chain, zone changes (`GameClientTests`) | Player builds: prefabs load in the Editor only; asset bundles are the next step |
| Played in Unity 2021.3.18f1 on Linux against the rewrite server and the live database: login, world, character list, Qeynos drawn with its textures and the citizens' models, 182 entities, walking | Player race models (`hum`, `trm`...): they are in `global_chr`, not in the zone exports; players and some NPCs are capsules |
| Entities drawn at interpolated positions, the local player moved from input on the zone's collision mesh (ground, steps up to 6 units, walls at waist height, edge of the zone) | Doors and objects do not block (not in the collision mesh); no jumping or swimming |
| Race → Lantern model code for the races checked in the exports (`ModelCodes`) | Animations (idle pose only), equipment, nameplates, chat, combat |
| EverQuest ↔ Unity coordinates with Lantern's 0.5 world scale (`Coordinates`) | The full model code table (unknown races draw the fallback model) |
