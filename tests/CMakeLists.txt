# Portable unit tests (no MySQL, no Windows): run with `ctest --test-dir build`.
add_executable(sql_escape_test sql_escape_test.cpp)
target_include_directories(sql_escape_test PRIVATE ../Common/Include)
add_test(NAME sql_escape COMMAND sql_escape_test)
