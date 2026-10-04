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
