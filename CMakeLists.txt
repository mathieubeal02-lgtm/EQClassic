cmake_minimum_required(VERSION 3.21)
project(EQClassic LANGUAGES C CXX)

# EQClassic server emulator (EverQuest Trilogy). Windows/x86 only, except the `azone`
# map tool which is portable. See docs/BUILD.md.

option(EQC_BUILD_SERVERS   "Build login/world/zone/SharedMemory (Windows only)" ${WIN32})
option(EQC_BUILD_AZONE     "Build the azone map tool"                           ON)
option(EQC_BUILD_MINILOGIN "Also build the MINILOGIN variant of the login server (stale, may not compile)" OFF)
option(EQC_COPY_RUNTIME_DLLS "Copy dependency DLLs next to the executables after build" ON)
set(EQC_DEPENDENCIES_DIR "${CMAKE_SOURCE_DIR}/Dependencies" CACHE PATH
    "Folder with mysql/, zlib/, Perl/, openssl/ (contents of Dependencies.zip)")

set(CMAKE_CXX_STANDARD 14)
set(CMAKE_CXX_STANDARD_REQUIRED OFF)
set(CMAKE_CXX_EXTENSIONS ON)

# Same runtime as the .vcxproj files: static CRT in Debug (/MTd), DLL CRT otherwise (/MD).
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
if(NOT CMAKE_CONFIGURATION_TYPES AND NOT CMAKE_BUILD_TYPE)
  set(CMAKE_BUILD_TYPE Debug CACHE STRING "" FORCE)
endif()

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

if(MSVC)
  add_compile_options(/MP /EHsc /W3)
  add_compile_definitions(_CRT_SECURE_NO_DEPRECATE _CRT_SECURE_NO_WARNINGS _WINSOCK_DEPRECATED_NO_WARNINGS)
  if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(WARNING "EQClassic has only ever been built as 32-bit (Win32). Configure with -A Win32.")
  endif()
endif()

include(cmake/EQCDependencies.cmake)

if(EQC_BUILD_SERVERS)
  if(NOT WIN32)
    message(FATAL_ERROR "The servers only build on Windows (Winsock, SharedMemory DLL). Use -DEQC_BUILD_SERVERS=OFF to build azone only.")
  endif()
  add_subdirectory(SharedMemory)
  add_subdirectory(World)
  add_subdirectory(Zone)
  add_subdirectory(LS/Login)
endif()
if(EQC_BUILD_AZONE)
  add_subdirectory(Utils/azone)
endif()

# `cmake --install build --prefix <server dir>` lays out a runnable server folder.
if(EQC_BUILD_SERVERS)
  install(FILES Zone/spdat.eff Zone/spells_en.txt Zone/Boot5zones.bat DESTINATION .)
  install(DIRECTORY Zone/cfg DESTINATION .)
  install(FILES LS/Login/loginserver.sql DESTINATION sql)
endif()
