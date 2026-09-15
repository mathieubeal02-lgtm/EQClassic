# legacy/ — code kept for reference, not compiled

Moved here during the Phase 0 cleanup (Sept 2026). Nothing in this folder is referenced by
`EQCEmu.sln` or any `.vcxproj`.

- `LS/common/`, `LS/zone/` — parts of the older EQEmu-derived tree under `LS/` that the
  **Login** project does not use. What stayed in `LS/common` and `LS/zone` is the transitive
  `#include` closure of `LS/Login/*.cpp` plus the `.cpp` files listed in `LS/Login/Login.vcxproj`
  (computed statically, ignoring `#ifdef`, i.e. conservative). `LS/zone` now holds only
  12 headers pulled in by `LS/common/database.h` and `EMuShareMem.h`.
- `makefiles/` — old GNU makefiles for World, Zone, Login and LS/zone. They reference
  `../PEQEmu/` and files that no longer exist, so they never built from this tree; kept
  for their compiler defines and link flags, useful for a future CMake port.

Deleted outright (not here): VS6/VS2005 artifacts (`*.dsw`, `*.dsp`, `*.opt`, `*.plg`,
`*.vcproj`, `*.ncb`), per-user `*.vcxproj.user`, and `Zone/Zone_Exceptions.txt`
(a 2009 production exception log containing player names). All recoverable from git history.
