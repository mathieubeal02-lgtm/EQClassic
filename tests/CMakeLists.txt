# Portable unit tests (no MySQL, no Windows): run with `ctest --test-dir build`.
add_executable(sql_escape_test sql_escape_test.cpp)
target_include_directories(sql_escape_test PRIVATE ../Common/Include)
add_test(NAME sql_escape COMMAND sql_escape_test)

# Fragment reassembly bounds (FragmentLimits.h) and FragmentGroup completion logic.
add_executable(fragment_test fragment_test.cpp
    ../Common/Source/FragmentGroup.cpp
    ../Common/Source/Fragment.cpp)
target_include_directories(fragment_test PRIVATE ../Common/Include ../EQC/Include)
# linked_list.h needs this one define from config.h (not included here).
target_compile_definitions(fragment_test PRIVATE ZONE_FREEZE_DEBUG=0)
add_test(NAME fragments COMMAND fragment_test)

# Melee combat formulas (Zone/Source/CombatFormulas.cpp), pure functions.
add_executable(combat_test combat_test.cpp ../Zone/Source/CombatFormulas.cpp)
add_test(NAME combat COMMAND combat_test)

# 16-bit sequence/ack ordering across the wrap (SequenceMath.h, used by EQPacketManager).
add_executable(sequence_test sequence_test.cpp)
add_test(NAME sequence COMMAND sequence_test)

# Two packet managers back to back across the 16-bit sequence wrap (EQPacketManager.cpp).
add_executable(packet_manager_test packet_manager_test.cpp
    ../Common/Source/EQPacket.cpp
    ../Common/Source/EQPacketManager.cpp
    ../Common/Source/Fragment.cpp
    ../Common/Source/FragmentGroup.cpp
    ../Common/Source/FragmentGroupList.cpp
    ../Common/Source/timer.cpp
    ../Common/Source/packet_dump.cpp)
target_include_directories(packet_manager_test PRIVATE ../Common/Include ../EQC/Include)
target_compile_definitions(packet_manager_test PRIVATE ZONE_FREEZE_DEBUG=0)
if(WIN32)
  target_link_libraries(packet_manager_test PRIVATE ws2_32)
endif()
add_test(NAME packet_manager COMMAND packet_manager_test)
