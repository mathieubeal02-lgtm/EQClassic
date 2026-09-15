# SharedMemory.dll: item data shared between world and zone processes.
add_library(SharedMemory SHARED
    Source/DLLMain.cpp
    Source/SharedMemory.cpp
    Source/SharedMemoryItems.cpp
    ../Common/Source/database.cpp
    ../Common/Source/DatabaseHandler.cpp
    ../EQC/Sources/EQCException.cpp
    ../EQC/Sources/EQCUtils.cpp
    ../Common/Source/Item.cpp
    ../Common/Source/Logger.cpp
    ../Common/Source/MiscFunctions.cpp
    ../Common/Source/Mutex.cpp
)
target_include_directories(SharedMemory PUBLIC Include PRIVATE ../Common/Include ../EQC/Include)
target_compile_definitions(SharedMemory PRIVATE WIN32 _WINDOWS _USRDLL SHAREDMEMORY_EXPORTS UNICODE _UNICODE)
target_link_libraries(SharedMemory PRIVATE ws2_32 eqc::mysql eqc::zlib)
install(TARGETS SharedMemory RUNTIME DESTINATION .)
