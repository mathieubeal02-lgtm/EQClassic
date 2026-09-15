# world.exe: accounts, character select, zone list, guilds, boats.
add_executable(world
    Source/Boat.cpp
    Source/BoatsManager.cpp
    Source/client.cpp
    Source/client_process.cpp
    Source/ClientList.cpp
    Source/console.cpp
    Source/languages.cpp
    Source/LoginServer.cpp
    Source/LoginServer_process.cpp
    Source/TimeOfDay.cpp
    Source/World.cpp
    Source/WorldGuildManager.cpp
    Source/WorldGuildManager_Packets.cpp
    Source/WorldGuildManager_Process.cpp
    Source/WorldGuildManager_ZoneProcess.cpp
    Source/ZoneServer.cpp
    Source/zoneserver_process.cpp
    Source/ClientNetwork.cpp
    Source/ConsoleList.cpp
    Source/net.cpp
    Source/WorldGuildNetworkManager.cpp
    Source/ZSList.cpp
    Source/WorldGuildDatabaseManager.cpp
    ../Common/Source/classes.cpp
    ../Common/Source/database.cpp
    ../Common/Source/DatabaseHandler.cpp
    ../Common/Source/Item.cpp
    ../Common/Source/Logger.cpp
    ../Common/Source/MiscFunctions.cpp
    ../Common/Source/moremath.cpp
    ../Common/Source/Mutex.cpp
    ../Common/Source/packet_dump.cpp
    ../Common/Source/packet_dump_file.cpp
    ../Common/Source/races.cpp
    ../Common/Source/timer.cpp
    ../Common/Source/EQPacket.cpp
    ../Common/Source/EQPacketManager.cpp
    ../Common/Source/Fragment.cpp
    ../Common/Source/FragmentGroup.cpp
    ../Common/Source/FragmentGroupList.cpp
    ../Common/Source/GuildNetwork.cpp
    ../Common/Source/packet_functions.cpp
    ../Common/Source/PlayerPacketFactory.cpp
    ../Common/Source/ServerPacketFactory.cpp
    ../Common/Source/GuildDatabase.cpp
    ../EQC/Sources/EQCException.cpp
    ../EQC/Sources/EQCUtils.cpp
    ../EQC/Sources/PacketUtils.cpp
)
target_include_directories(world PRIVATE Include Network ../Common/Include ../EQC/Include ../SharedMemory/Include)
# NOTE: the Release .vcxproj configuration dropped WORLD/EQC_SHAREDMEMORY; only Debug was ever
# used upstream, so both configurations get the Debug definitions here.
target_compile_definitions(world PRIVATE WIN32 _CONSOLE WORLD EQC_SHAREDMEMORY UNICODE _UNICODE
  $<$<NOT:$<CONFIG:Debug>>:RELEASE>)
target_link_libraries(world PRIVATE ws2_32 eqc::mysql eqc::zlib SharedMemory)
eqc_copy_runtime_dlls(world)
install(TARGETS world RUNTIME DESTINATION .)
