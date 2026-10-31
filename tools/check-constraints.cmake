# check-constraints.cmake — the README's rules for native code, checked by the build.
#
# Run as a test:  cmake -DBC_ROOT=<repo> -P tools/check-constraints.cmake
#
# These are rules that are obeyed for months and then broken by one convenient edit, with nothing
# to show for it until the CUDA port or a precision problem. So they are checked, not trusted.
# The checks are textual and deliberately simple; a line that must break one carries the marker
# named in its message, with the reason beside it.

if(NOT DEFINED BC_ROOT)
  get_filename_component(BC_ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
endif()

set(FAILURES 0)

function(fail rule msg)
  message("  FAIL  [rule ${rule}] ${msg}")
  math(EXPR FAILURES "${FAILURES}+1")
  set(FAILURES ${FAILURES} PARENT_SCOPE)
endfunction()

file(GLOB_RECURSE NATIVE_SOURCES
     "${BC_ROOT}/native/include/*.h"
     "${BC_ROOT}/native/src/*.h"
     "${BC_ROOT}/native/src/*.cpp")

foreach(f ${NATIVE_SOURCES})
  file(STRINGS "${f}" lines)
  set(prev "")
  foreach(line ${lines})
    # Comment lines are prose, and prose may name what it forbids.
    if(line MATCHES "^[ \t]*(/[*/]|[*])")
      set(prev "${line}")
      continue()
    endif()

    # Rule 1: no allocation.
    if(line MATCHES "(^|[^A-Za-z0-9_])(malloc|calloc|realloc|free|new|delete)[ \t]*[(\\[ ]"
       OR line MATCHES "std::(vector|string|unique_ptr|shared_ptr|make_unique|make_shared|map|list|deque)")
      fail("1" "${f}: allocation: ${line}")
    endif()

    # Rule 2: no MAX_ constants and no arrays dimensioned by a literal. Brackets are written as "."
    # to stay clear of CMake's escaping; type, name, digits and a semicolon are all required.
    if(line MATCHES "#[ \t]*define[ \t]+[A-Z_]*MAX_[A-Z0-9_]*")
      fail("2" "${f}: a MAX_ constant: ${line}")
    endif()
    if(line MATCHES "^[ \t]*(const[ \t]+)?[A-Za-z_][A-Za-z0-9_]*[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]*.[ \t]*[0-9]+[ \t]*.[ \t]*;")
      if(NOT line MATCHES "BC_FIXED_OK" AND NOT prev MATCHES "BC_FIXED_OK")
        fail("2" "${f}: a fixed-size array without a BC_FIXED_OK reason: ${line}")
      endif()
    endif()

    # Rule 3: exceptions and virtual dispatch do not port to a kernel.
    if(line MATCHES "(^|[^A-Za-z0-9_])(throw|try|catch|virtual)([^A-Za-z0-9_]|$)")
      fail("3" "${f}: exceptions or virtuals: ${line}")
    endif()

    # Rule 4: double precision.
    if(line MATCHES "(^|[^A-Za-z0-9_])float([^A-Za-z0-9_]|$)")
      fail("4" "${f}: float: ${line}")
    endif()

    set(prev "${line}")
  endforeach()
endforeach()

# Rule 3: every inline function in a header carries BC_HD, or device code could not call it.
file(GLOB NATIVE_HEADERS "${BC_ROOT}/native/src/*.h")
foreach(h ${NATIVE_HEADERS})
  file(STRINGS "${h}" lines)
  foreach(line ${lines})
    if(line MATCHES "^(static )?inline ")
      fail("3" "${h}: an inline without BC_HD: ${line}")
    endif()
  endforeach()
endforeach()

if(FAILURES GREATER 0)
  message(FATAL_ERROR "${FAILURES} constraint failure(s)")
endif()
message("constraints: all hold")
