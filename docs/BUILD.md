# Building and running

## Toolchain

- Visual Studio 2022 Community (toolset **v143**, Windows SDK 10.0), **Win32 (x86)** only.
- Two build paths, both kept in sync (CI builds both):
  - **CMake** (recommended): `cmake -S . -B build -G "Visual Studio 17 2022" -A Win32`,
    then `cmake --build build --config Debug` and `cmake --install build --config Debug --prefix server`
    to get a runnable server folder (exes + dependency DLLs + everything in `runtime/`). Options: `EQC_DEPENDENCIES_DIR`, `EQC_BUILD_MINILOGIN`, `EQC_COPY_RUNTIME_DLLS`,
    `EQC_BUILD_SERVERS` (OFF on Linux: only `azone` builds there).
  - **Legacy**: open `EQCEmu.sln`, Build → Build Solution (Debug|Win32). Outputs `login.exe`,
    `world.exe`, `zone.exe`, `SharedMemory.dll`.
- Only the Debug configuration was ever used upstream. The CMake build applies the Debug
  preprocessor definitions (`WORLD`, `EQC_SHAREDMEMORY`, …) to Release too, which the old
  `.vcxproj` Release configurations did not.
- CI: `.github/workflows/build.yml` builds CMake Debug/Release and the legacy solution on
  `windows-2022` (VS 2022 / v143; `windows-latest` is now VS 2026 without v143), plus `azone` on Linux. Dependencies.zip is downloaded from archive.org and cached.

## Dependencies (`Dependencies/`, not in git)

Project files expect this layout (paths from `*.vcxproj`):

```
Dependencies/
  mysql/include, mysql/lib/libmysql.lib (+ .dll)      MySQL C client 5.7.17, x86 (MySQL 8 server works with mysql_native_password)
  zlib/include,  zlib/lib/zdll.lib | zlib.lib, zlib1.dll  zlib 1.2.3, x86 (zdll.lib in Debug, zlib.lib in Release, as in the .vcxproj)
  Perl/lib/CORE/perl512.lib, Perl/bin/perl512.dll      ActivePerl 5.12.3, x86
  openssl/include, openssl/lib/{libeay32,ssleay32}.lib, openssl/bin/*.dll   OpenSSL 0.9.8k, x86 (Login only)
```

All binaries are 32-bit (PE32 i386). Exact contents verified from the bundle below
(`Dependencies.zip`, 316 MB, uploaded May 2025); it extracts to exactly the layout above.
- https://archive.org/details/dependencies_202505
- https://drive.google.com/file/d/0B4YtK9YXaHvQbU93Z25kY2J4WWM/view?usp=sharing

ActivePerl 5.12 (x86) installer: http://eqemu.github.io/downloads/ActivePerl-5.12.3.1204-MSWin32-x86-294330.msi

Known quirk reported by the community (N0ctrnl/EQTrilogy-Old): compile against Perl 5.12,
but quests may need Perl 5.14; the workaround used was renaming `Perl514.dll` → `Perl512.dll`.
To be verified.

## External data (not in git)

| What | Where |
|---|---|
| World/zone database dump (`eqclassic.sql`) | https://github.com/erfg12/eqclassic_db (`sql/`) — import with MySQL Workbench → Server → Data Import |
| Login schema | `LS/Login/loginserver.sql` (in repo) |
| Map files (`.map`, required or NPCs do not spawn) | https://sourceforge.net/projects/eqemulator/files/EQEmulator%20Map%20Files/EQEmulator%20Map%20Pack%201.0/Maps.tar.gz/download |
| Perl quests (PEQ Velious pack) | https://sourceforge.net/projects/eqemuquests/files/EQEmu%20Quest%20Packs/PEQ%20Velious%20Quests/peq-velious-rc1-quests.zip/download |
| Pre-built server package (binaries + ini + cfg) | https://archive.org/details/open-eqc-server · https://drive.google.com/file/d/1LvigV1hwdo729SH0wutvYs9xc9tVaUKV/view?usp=sharing |
| Client: EverQuest Trilogy CDs | https://archive.org/details/EverQuestTrilogy |
| EQWindow (EQW.exe, required to connect) | https://archive.org/details/eqwindow-beta-v-2.32 |
| dgVoodoo2 / d3drm.dll (client rendering on modern Windows) | https://dege.freeweb.hu/dgVoodoo2/dgVoodoo2/ · https://dege.freeweb.hu/dgVoodoo2/bin/D3DRM.zip |
| Upstream documentation | https://newagesoldier.com/EQClassic/ (redirect target of erfg12.github.io/EQClassic) |
| Fork with compile fixes | https://github.com/N0ctrnl/EQTrilogy-Old |

## Runtime setup

See `docs/RUNBOOK.md` (step-by-step) and `runtime/README.md` (what each file is).
