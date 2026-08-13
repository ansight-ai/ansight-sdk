cmake_minimum_required(VERSION 3.22.1)
project(ansight_crash_handler C)

add_library(
    ansight-crash-handler
    SHARED
    ansight_crash_signal.c
)
