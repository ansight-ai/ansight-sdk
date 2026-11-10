#!/usr/bin/env python3
"""Compile and exercise the real Apple and Android signal handlers in child processes."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
compiler = shutil.which("clang") or shutil.which("cc")
if not compiler:
    raise SystemExit("A C11 compiler is required.")
java_home = os.environ.get("JAVA_HOME")
if not java_home and Path("/usr/libexec/java_home").exists():
    java_home = subprocess.check_output(["/usr/libexec/java_home"], text=True).strip()
if not java_home:
    raise SystemExit("JAVA_HOME is required to compile the Android JNI entry points.")
include = Path(java_home) / "include"
platform_include = include / ("darwin" if os.uname().sysname == "Darwin" else "linux")
with tempfile.TemporaryDirectory(prefix="ansight-crash-tests-") as temporary:
    for platform in ["apple", "android"]:
        output = Path(temporary) / platform
        command = [compiler, "-std=gnu11", "-Wall", "-Wextra", "-Werror", "-pthread",
                   "-I", str(root / "src/ios/Sources/CAnsightCrashCapture/include"),
                   "-I", str(include), "-I", str(platform_include)]
        if platform == "android":
            command.append("-DANSIGHT_TEST_ANDROID")
        command += [str(root / "src/native-tests/crash_signal_tests.c"), "-o", str(output)]
        subprocess.run(command, check=True)
        print(f"{platform}:", flush=True)
        subprocess.run([str(output)], check=True, timeout=30)
