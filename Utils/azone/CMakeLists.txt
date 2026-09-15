# azone: builds .map collision files from client .s3d/.wld zone archives. Portable (C/C++ + zlib).
add_executable(azone azone.cpp s3d.c wld.c)
target_link_libraries(azone PRIVATE eqc::zlib)
if(MSVC)
  target_compile_definitions(azone PRIVATE WIN32 _MBCS)
  # The .vcxproj used the DLL CRT in Debug for this tool.
  set_property(TARGET azone PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
endif()
install(TARGETS azone RUNTIME DESTINATION tools)
