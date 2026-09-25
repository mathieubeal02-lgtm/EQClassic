# eqbot: headless Trilogy test client (POSIX sockets + OpenSSL DES). Linux/macOS only; it talks to
# servers running anywhere (Windows, or Wine as in docs/RUNBOOK.md).
find_package(OpenSSL REQUIRED COMPONENTS Crypto)

add_executable(eqbot
    eqbot.cpp
    EqSession.cpp
    ../../Common/Source/EQPacket.cpp
    ../../Common/Source/EQPacketManager.cpp
    ../../Common/Source/Fragment.cpp
    ../../Common/Source/FragmentGroup.cpp
    ../../Common/Source/FragmentGroupList.cpp
    ../../Common/Source/timer.cpp
    ../../Common/Source/packet_dump.cpp)
target_include_directories(eqbot PRIVATE ../../Common/Include ../../EQC/Include)
# The protocol sources rely on config.h being force-included, as the server projects do.
target_compile_options(eqbot PRIVATE -include config.h -w)
target_link_libraries(eqbot PRIVATE OpenSSL::Crypto)
