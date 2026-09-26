# Unity client (M4) — setup

The client logic lives in `rewrite/src/ClientCore` (connection flow, zone state, interpolation,
local movement), tested without Unity. The Unity project only draws: three scripts in
`unity/Assets/EQClassic/Scripts` on top of LanternUnityTools, which imports the zones and models.

## Requirements

- Unity Hub and **Unity 2021.3.18f1**. LanternUnityTools needs this version and URP 12.1.10; newer
  versions fail to compile its EQ shaders. It needs a Unity account (Personal licence is enough).
- The Trilogy client files, for example `~/eq-client`.
- The .NET SDK and `rsync`. On Linux, `libgdiplus` for texture conversion.

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
5. Run **EQ > Assets > Import Zone** with a zone short name, for example `qeynos2`.
6. Run **EQ > Assets > Import Characters**.
7. Open an empty scene and press **Play**. The login screen appears. Enter the server address, an
   account (for example `bot` / `bot`), pick the world and a character. You then walk with
   WASD or the arrows and turn with Q/E. The NPCs follow their paths as the server moves them.

`setup-client.sh` can be run again at any time, for example after changing the scripts or the
libraries. It never touches Unity's `Library/` cache.

## What exists and what does not yet

| Works (tested) | Not yet |
|---|---|
| Login, world and zone connection chain, zone changes (`GameClientTests`) | Checked in Unity itself: the scripts are only compiled against stubs (`unity/StubCheck`) |
| Entities drawn at interpolated positions, the local player moved from input | Player builds: prefabs load in the Editor only; asset bundles are the next step |
| Race → Lantern model code for the races checked in the exports (`ModelCodes`) | Animations (idle pose only), equipment, nameplates, chat, combat |
| EverQuest ↔ Unity coordinates with Lantern's 0.5 world scale (`Coordinates`) | The full model code table (unknown races draw the fallback model) |
