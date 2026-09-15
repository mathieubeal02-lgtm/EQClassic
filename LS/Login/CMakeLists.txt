# login.exe: login server (DES-encrypted client auth via OpenSSL, LOGINCRYPTO).
set(LOGIN_SOURCES
    client.cpp
    EQCrypto.cpp
    logindatabase.cpp
    LWorld.cpp
    net.cpp
    ../common/crc32.cpp
    ../common/database.cpp
    ../common/dbcore.cpp
    ../common/DBMemLeak.cpp
    ../common/debug.cpp
    ../common/EQNetwork.cpp
    ../common/md5.cpp
    ../common/MiscFunctions.cpp
    ../common/Mutex.cpp
    ../common/packet_dump.cpp
    ../common/packet_functions.cpp
    ../common/TCPConnection.cpp
    ../common/timer.cpp
)

add_executable(login ${LOGIN_SOURCES})
target_compile_definitions(login PRIVATE WIN32 _CONSOLE _MBCS LOGINCRYPTO INVERSEXY
  $<$<CONFIG:Debug>:EQC_SHAREDMEMORY>)
target_link_libraries(login PRIVATE odbc32 odbccp32 wsock32 eqc::mysql eqc::zlib eqc::openssl)
eqc_copy_runtime_dlls(login)
install(TARGETS login RUNTIME DESTINATION .)

# MiniLogin: local-only login without crypto (the old "MiniLogin|Win32" configuration).
if(EQC_BUILD_MINILOGIN)
  add_executable(minilogin ${LOGIN_SOURCES})
  target_compile_definitions(minilogin PRIVATE WIN32 _CONSOLE _MBCS MINILOGIN)
  target_link_libraries(minilogin PRIVATE odbc32 odbccp32 wsock32 eqc::mysql eqc::zlib)
  install(TARGETS minilogin RUNTIME DESTINATION .)
endif()
