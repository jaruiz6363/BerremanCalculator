# BerremanCalculator — native evaluation engine.
#
# CMake from the start, because Windows, macOS and Linux are all targets.

cmake_minimum_required(VERSION 3.20)
project(berreman LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Warnings are errors: the engine is arithmetic that has to be right, and a warning is a defect
# report.
if(MSVC)
  add_compile_options(/W4 /WX)
else()
  add_compile_options(-Wall -Wextra -Wpedantic -Werror)
endif()

set(BC_SOURCES
  native/src/bc_plan.cpp
  native/src/bc_evaluate.cpp)

# The static library the native tests link, and the shared one C# loads, from the same sources,
# so the tests cannot drift onto a different build of the engine.
add_library(berreman STATIC ${BC_SOURCES})
add_library(berreman_shared SHARED ${BC_SOURCES})

# NAMED berreman_native, NOT berreman: the CLI's executable is `berreman`, and .NET writes a managed
# berreman.dll beside it, which P/Invoke would find first. (ThinFilmDesigner hit exactly this.)
set_target_properties(berreman_shared PROPERTIES OUTPUT_NAME berreman_native)

foreach(t berreman berreman_shared)
  target_include_directories(${t} PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/native/include
    ${CMAKE_CURRENT_SOURCE_DIR}/native/src)
endforeach()

enable_testing()

add_executable(test_skeleton tests/native/test_skeleton.cpp)
target_link_libraries(test_skeleton PRIVATE berreman)
add_test(NAME skeleton COMMAND test_skeleton)

# Engine design, Section 6: the tests written before the engine. Header-only engine parts are
# tested directly; test_support.h holds the tests' own, independent numerics.
foreach(t delta isotropic sweep outer fields)
  add_executable(test_${t} tests/native/test_${t}.cpp)
  target_link_libraries(test_${t} PRIVATE berreman)
  add_test(NAME ${t} COMMAND test_${t})
endforeach()

# The README's rules, checked by the build rather than remembered.
add_test(NAME constraints COMMAND "${CMAKE_COMMAND}"
         "-DBC_ROOT=${CMAKE_CURRENT_SOURCE_DIR}"
         -P "${CMAKE_CURRENT_SOURCE_DIR}/tools/check-constraints.cmake")
