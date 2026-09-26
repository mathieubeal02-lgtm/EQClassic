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
# 1. Export the zones you want (their creatures come with each zone's _chr file), and the
#    player races, which live in the global character archives
tools/lantern/extract.sh ~/eq-client qeynos2 grobb permafrost
tools/lantern/extract.sh ~/eq-client global_chr.s3d global2_chr.s3d global3_chr.s3d global4_chr.s3d

# 2. Assemble the Unity project in build/unity-client (LanternUnityTools + our scripts and DLLs + exports)
tools/unity/setup-client.sh

# 3. Start the servers (login, world and zones), with the collision meshes
cd rewrite && dotnet run --project src/Server -- \
  --db "Server=127.0.0.1;User ID=eqc;Password=eqc;Database=eqclassic;SslMode=None" \
  --lantern ../build/lantern-work/Exports
```

4. In Unity Hub, open `build/unity-client`. The first import takes several minutes.
5. Import the zones and characters with `tools/unity/import-zones.sh --characters all` (Unity closed;
   about an hour for the 120 Trilogy zones). It copies a few exports at a time into
   `Assets/EQAssets`, runs the LanternUnityTools importers through our editor script (without their
   dialogs, under the invariant culture: on a French system theirs fail with a FormatException),
   then removes the copies. The exports themselves stay in `build/lantern-work/Exports` (or
   `EQC_EXPORTS`): with all of them under `Assets`, Unity spends hours scanning 125,000 files.
6. Open an empty scene and press **Play**. The login screen appears. Enter the server address, an
   account (for example `bot` / `bot`), pick the world and a character. You then walk with
   WASD or the arrows and turn with Q/E. The NPCs follow their paths as the server moves them.

8. For a standalone client, run **EQClassic > Build Linux Player** (or **Build Windows Player**,
   which needs Unity's Windows build support module, see below). It writes asset bundles of the imported
   zones and characters plus the zones' collision meshes to `Assets/StreamingAssets/EQClassic`,
   then the player (Mono backend) to `build/unity-player/linux/EQClassic.x86_64`. Copy that
   folder to play elsewhere; nothing else is needed on the player's machine.

`setup-client.sh` can be run again at any time, for example after changing the scripts or the
libraries. It never touches Unity's `Library/` cache or the imported assets
(`Assets/Content/AssetBundleContent`).

Scripted sessions (what the test below used), from the repository root:

```sh
Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBatch.ImportAll -quit
Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBuild.BuildLinux -quit
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

| No Windows build support for an editor installed outside the Hub ("Module installation is only supported for editors installed with Unity Hub") | The Linux editor's `windows-mono` module is published as the macOS `.pkg` (Unity release API) | Download `MacEditorTargetInstaller/UnitySetup-Windows-Mono-Support-for-Editor-<version>.pkg`, extract it (`bsdtar -xf`, then `gzip -dc TargetSupport.pkg.tmp/Payload \| cpio -id`) into `Editor/Data/PlaybackEngines/WindowsStandaloneSupport` |

A very high open-file limit (`ulimit -n` 524288) is also better lowered to 4096 before starting
the editor.

## What exists and what does not yet

| Works (tested) | Not yet |
|---|---|
| Login, world and zone connection chain, zone changes (`GameClientTests`) | The Windows build is built but not yet played on Windows (only 32-bit Wine here) |
| Qeynos, Grobb and Permafrost imported and entered from the Linux build; a warm light follows the player (dungeons are playable) | Light sources carried as items (torches) are not modelled |
| Placed objects (trees, lamp posts, crates, furniture: 480 in Qeynos) drawn from the Lantern object list; LanternUnityTools leaves them out of the zone prefab | Objects keep their default colours (Lantern's per-instance vertex colours are not applied) |
| Standalone Linux build (173 MB with Qeynos and 64 character models) played against the rewrite server: login, world, zone, models from the asset bundles | |
| Two standalone clients in Qeynos at once: each sees the other player walk (183 entities: 181 NPCs, 2 players) | |
| Command line: `EQClassic.x86_64 -host <address> -port <port> -fingerprint <key> -user <name>` prefills the screens (never the password) | |
| Played in Unity 2021.3.18f1 on Linux against the rewrite server and the live database: login, world, character list, Qeynos drawn with its textures and the citizens' models, 182 entities, walking | Races missing from `ModelCodes` or from the exports draw a capsule |
| Entities drawn at interpolated positions, the local player moved from input on the zone's collision mesh (ground, steps up to 6 units, walls at waist height, edge of the zone), which includes the placed objects that have a collision mesh (in Qeynos, the trees) | Closed doors do not block; no jumping or swimming |
| Melee: **Tab** targets the nearest NPC (again to cycle), **T** faces the target, **F** toggles auto-attack; hit points and the target's health on screen, combat lines as the Trilogy client wrote them, attack and flinch animations | Spells, experience, loot, corpses; Esc (clear target) is kept by the Editor in Play mode |
| Spells: **B** opens the spell book (memorise a spell by clicking a gem number), **1-8** or a click on a gem casts it at the target (or yourself for a heal), mana bar, casting bar, **Scribe** button on spell scrolls in the inventory (new casters start with theirs) | Written, to check in Unity; direct damage, heals and buffs (buff window at the top right) so far |
| Doors from the legacy `doors` table: **U** uses the nearest one; the server applies the legacy rules (toggle, trigger door, closed again after 12 s, locked doors refused with the legacy message, teleport doors) and every player sees it swing; server messages at the bottom of the screen | Door animations approximate the open types (swing 90°, or slide up for lifts and gates); no key or lock picking until inventory exists |
| Race → Lantern model code for the races checked in the exports (`ModelCodes`), player races from `global_chr`; models stand on the ground (lifted by their bounds) | Equipment, nameplates, chat, combat animations |
| Stand, walk and run animations from each entity's speed (Lantern's `CharacterAnimationController`); the camera comes closer when a wall of the collision mesh hides the player | Crates, barrels and lamp posts are not solid: their Lantern collision files are empty (no vertices) |
| EverQuest ↔ Unity coordinates with Lantern's 0.5 world scale (`Coordinates`) | The full model code table (unknown races draw the fallback model) |
