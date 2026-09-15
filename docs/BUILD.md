# Building and running

## Toolchain

- Visual Studio 2022 Community (toolset **v143**, Windows SDK 10.0), **Win32 (x86)** only.
- Open `EQCEmu.sln`, Build → Build Solution. Outputs `login.exe`, `world.exe`, `zone.exe`,
  `SharedMemory.dll` (Debug/Release per project settings).

## Dependencies (`Dependencies/`, not in git)

Project files expect this layout (paths from `*.vcxproj`):

```
Dependencies/
  mysql/include, mysql/lib        libmysql.lib   (MySQL 5.1 client; MySQL 8 server works with mysql_native_password)
  zlib/include,  zlib/lib         zdll.lib (Debug) / Zlib.lib (Release) — headers in repo are zlib 1.2.3 (Common/Include/zlib.h)
  Perl/lib/CORE                   perl512.lib    (ActivePerl 5.12.3.1204 MSWin32-x86)
  openssl/include (+ lib)         libeay32.lib, ssleay32.lib (Login only, OpenSSL 0.9.8 era API: openssl/des.h)
```

Ready-made bundle from the upstream maintainer ("EQClassic Compile Dependencies",
Dependencies.zip, ~300 MB, May 2025):
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

Server directory (next to the executables):

```
login.exe  world.exe  zone.exe  SharedMemory.dll
db.ini              [Database]  host= user= pass= database=        (Common/Source/database.cpp:68)
LoginServer.ini     [LoginServer] loginserver= worldname= account= password= locked= worldaddress=   (World/Source/net.cpp:181)
                    [LoginConfig] servermode=Standalone|Master|Slave|Mesh ...                          (LS/Login/net.cpp:238)
MiniLoginAccounts.ini   (Login built with MINILOGIN)
spells_en.txt  spdat.eff   (copied from the client; zones crash without spells_en.txt)
cfg/<zone>.cfg          (Zone/cfg)
maps/                   (.map files; README says /maps/maps)
quests/<zone>/*.pl  quests/plugins/*.pl  quests/<zone>/player.pl
```

1. Create MySQL schema `eqclassic`, import `eqclassic.sql`, then `loginserver.sql`; add an account.
2. Fill `db.ini` and `LoginServer.ini` (127.0.0.1 for local, LAN/public IP otherwise).
3. Start in order: `login.exe`, `world.exe`, then `Boot5zones.bat`
   (`zone <zone_name|.> <address> <port> <worldaddress>`, default port 7996).
4. Connect the Trilogy client through EQW.exe (the `patchme` suffix disconnects).

Ports: Login 5999 (`LOGIN_PORT`), World 9000 + client UDP, Zone 7996 by default.
