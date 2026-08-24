cmake_minimum_required(VERSION 3.22.1)
project(ansight_crash_handler C)

add_library(
    ansight-crash-handler
    SHARED
    ansight_crash_signal.c
)

# NDK r27 and earlier default to 4 KB ELF LOAD-segment alignment. Ansight is
# distributed as a prebuilt SDK, so consumers cannot correct this downstream.
target_link_options(
    ansight-crash-handler
    PRIVATE
    "-Wl,-z,max-page-size=16384"
    "-Wl,-z,common-page-size=16384"
)
