# Imported targets for the prebuilt third-party libraries shipped in Dependencies.zip
# (see docs/BUILD.md). Layout expected under EQC_DEPENDENCIES_DIR:
#   mysql/include, mysql/lib/libmysql.lib          MySQL C client 5.7.17 (x86)
#   zlib/include,  zlib/lib/zdll.lib               zlib 1.2.3 (x86, import lib of zlib1.dll)
#   Perl/lib/CORE/perl512.lib                       ActivePerl 5.12.3 (x86)
#   openssl/include, openssl/lib/libeay32.lib, ssleay32.lib   OpenSSL 0.9.8k (x86)
# On non-Windows hosts only system zlib is looked up (for azone).

if(NOT WIN32)
  find_package(ZLIB REQUIRED)
  add_library(eqc::zlib ALIAS ZLIB::ZLIB)
  return()
endif()

set(_dep "${EQC_DEPENDENCIES_DIR}")
foreach(_probe mysql/include/mysql.h zlib/include/zlib.h Perl/lib/CORE/perl.h openssl/include/openssl/des.h)
  if(NOT EXISTS "${_dep}/${_probe}")
    message(FATAL_ERROR "Missing ${_dep}/${_probe}. Extract Dependencies.zip into ${_dep} "
                        "(https://archive.org/details/dependencies_202505) or set EQC_DEPENDENCIES_DIR.")
  endif()
endforeach()

add_library(eqc::mysql INTERFACE IMPORTED)
set_target_properties(eqc::mysql PROPERTIES
  INTERFACE_INCLUDE_DIRECTORIES "${_dep}/mysql/include"
  INTERFACE_LINK_LIBRARIES      "${_dep}/mysql/lib/libmysql.lib")

# zdll.lib is the import library of zlib1.dll and is the one that resolves deflate/inflate.
# (The .vcxproj Release configurations pointed at zlib.lib, which does not link: LNK2019 on
# _deflate/_inflate — the Release configurations were never used upstream.)
add_library(eqc::zlib INTERFACE IMPORTED)
set_target_properties(eqc::zlib PROPERTIES
  INTERFACE_INCLUDE_DIRECTORIES "${_dep}/zlib/include"
  INTERFACE_LINK_LIBRARIES      "${_dep}/zlib/lib/zdll.lib")

add_library(eqc::perl INTERFACE IMPORTED)
set_target_properties(eqc::perl PROPERTIES
  INTERFACE_INCLUDE_DIRECTORIES "${_dep}/Perl/lib/CORE"
  INTERFACE_LINK_LIBRARIES      "${_dep}/Perl/lib/CORE/perl512.lib")

add_library(eqc::openssl INTERFACE IMPORTED)
set_target_properties(eqc::openssl PROPERTIES
  INTERFACE_INCLUDE_DIRECTORIES "${_dep}/openssl/include"
  INTERFACE_LINK_LIBRARIES      "${_dep}/openssl/lib/libeay32.lib;${_dep}/openssl/lib/ssleay32.lib")

set(EQC_RUNTIME_DLLS
  "${_dep}/mysql/lib/libmysql.dll"
  "${_dep}/zlib/zlib1.dll" "${_dep}/zlib/zlib.dll"
  "${_dep}/Perl/bin/perl512.dll"
  "${_dep}/openssl/bin/libeay32.dll" "${_dep}/openssl/bin/ssleay32.dll")

# eqc_copy_runtime_dlls(<target>): copy the dependency DLLs next to <target> after it is built.
function(eqc_copy_runtime_dlls tgt)
  if(EQC_COPY_RUNTIME_DLLS)
    add_custom_command(TARGET ${tgt} POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different ${EQC_RUNTIME_DLLS} $<TARGET_FILE_DIR:${tgt}>
      COMMENT "Copying dependency DLLs next to ${tgt}" VERBATIM)
  endif()
endfunction()
unset(_dep)
