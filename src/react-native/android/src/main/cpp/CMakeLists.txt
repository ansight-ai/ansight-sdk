cmake_minimum_required(VERSION 3.13)

project(ansightreactnativememory)

add_library(ansightreactnativememory SHARED ansight_react_native_memory.cpp)
target_compile_features(ansightreactnativememory PRIVATE cxx_std_17)

target_include_directories(
  ansightreactnativememory
  PRIVATE
    "${REACT_NATIVE_DIR}/ReactCommon"
    "${REACT_NATIVE_DIR}/ReactCommon/jsi"
)

find_library(log-lib log)
target_link_libraries(ansightreactnativememory ${log-lib})
