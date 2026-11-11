#!/usr/bin/env python3
"""Build selected prepared benchmarks and retain exact commands and binary hashes."""

import argparse
import datetime
import hashlib
import json
import os
import subprocess
import shutil
from pathlib import Path


SDK_ROOT = Path(__file__).resolve().parents[2]


def build_scanner(output):
    result_directory = output / "results" / "prerequisites"
    result_directory.mkdir(parents=True, exist_ok=True)
    command = ["dotnet", "build", str(SDK_ROOT / "src/dotnet/Ansight.Build/Ansight.Build.csproj"),
               "-c", "Debug", f"-p:RestoreConfigFile={output / 'NuGet.Config'}",
               "-p:UseSharedCompilation=false", "-m:1", "-nodeReuse:false", "-v:minimal"]
    with (result_directory / "scanner-build.log").open("w") as log:
        process = subprocess.run(command, cwd=output, stdout=log, stderr=subprocess.STDOUT)
    (result_directory / "scanner-build.json").write_text(json.dumps({
        "command": command,
        "exitCode": process.returncode,
        "buildStatus": "passed" if process.returncode == 0 else "failed",
    }, indent=2) + "\n")
    return process.returncode == 0


def fingerprint(path):
    digest = hashlib.sha256()
    files = [path] if path.is_file() else sorted(candidate for candidate in path.rglob("*") if candidate.is_file())
    for candidate in files:
        if path.is_dir():
            digest.update(str(candidate.relative_to(path)).encode())
            digest.update(b"\0")
        with candidate.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    return {"sha256": digest.hexdigest(), "fileCount": len(files)}


def build(benchmark, platform, output, xcode_developer_directory):
    project = output / "upstream" / benchmark["source"] / benchmark["projects"][platform]
    result_directory = output / "results" / benchmark["id"] / platform
    result_directory.mkdir(parents=True, exist_ok=True)
    log_path = result_directory / "build.log"
    result_path = result_directory / "build.json"
    if result_path.exists():
        previous = json.loads(result_path.read_text())
        history = result_directory / "history" / previous.get("startedAtUtc", "unknown").replace(":", "-")
        history.mkdir(parents=True, exist_ok=True)
        shutil.copy2(result_path, history / "build.json")
        if log_path.exists():
            shutil.copy2(log_path, history / "build.log")
    command = ["dotnet", "build", str(project), "-c", "Debug",
               f"-p:RestoreConfigFile={output / 'NuGet.Config'}",
               "-p:RestoreSources=https://api.nuget.org/v3/index.json",
               "-p:UseSharedCompilation=false", "-m:1", "-nodeReuse:false", "-v:minimal"]
    command += [f"-p:{key}={value}" for key, value in benchmark.get("buildProperties", {}).items()]
    environment = os.environ.copy()
    if platform == "ios":
        command += ["--no-incremental", "-p:RuntimeIdentifier=iossimulator-arm64", "-p:BuildIpa=false",
                    "-p:EnableCodeSigning=true", "-p:CodesignKey=-"]
        environment["DEVELOPER_DIR"] = xcode_developer_directory

    record = {
        "schemaVersion": 1,
        "app": benchmark["id"],
        "platform": platform,
        "startedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "command": command,
        "workingDirectory": str(output),
        "developerDirectory": environment.get("DEVELOPER_DIR") if platform == "ios" else None,
        "log": str(log_path),
        "validationStatus": "not_run",
    }
    if benchmark.get("integrationStatus") == "pending":
        record.update({"buildStatus": "blocked", "reason": "Sample integration is not implemented yet."})
    else:
        with log_path.open("w") as log:
            process = subprocess.run(command, cwd=output, env=environment, stdout=log, stderr=subprocess.STDOUT)
        record["exitCode"] = process.returncode
        record["buildStatus"] = "passed" if process.returncode == 0 else "failed"
        if process.returncode == 0:
            pattern = f"**/{project.stem}.app" if platform == "ios" else "**/*-Signed.apk"
            binaries = sorted((project.parent / "bin/Debug").glob(pattern))
            record["binaries"] = [{"path": str(binary), **fingerprint(binary)} for binary in binaries]
            if not binaries:
                record.update({"buildStatus": "failed", "reason": "Build returned success without an installable binary."})

    record["finishedAtUtc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
    result_path.write_text(json.dumps(record, indent=2) + "\n")
    print(json.dumps({"app": benchmark["id"], "platform": platform, "buildStatus": record["buildStatus"],
                      "result": str(result_path)}), flush=True)
    return record["buildStatus"] == "passed"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=SDK_ROOT / ".ansight-validation/avalonia")
    parser.add_argument("--app", action="append", help="Benchmark id. Omit to build every benchmark.")
    parser.add_argument("--platform", action="append", choices=["ios", "android"])
    parser.add_argument("--xcode-developer-directory", default="/Applications/Xcode-26.3.0.app/Contents/Developer")
    arguments = parser.parse_args()
    output = arguments.output.resolve()
    manifest = json.loads((output / "samples.json").read_text())
    selected = [item for item in manifest["benchmarks"] if not arguments.app or item["id"] in arguments.app]
    missing = set(arguments.app or []) - {item["id"] for item in selected}
    if missing:
        parser.error(f"Unknown benchmark ids: {', '.join(sorted(missing))}")

    if not build_scanner(output):
        print("Ansight build-scanner prerequisite failed; see results/prerequisites/scanner-build.log.", flush=True)
        return 1

    results = []
    for benchmark in selected:
        for platform in arguments.platform or manifest["platforms"]:
            results.append(build(benchmark, platform, output, arguments.xcode_developer_directory))
    return 0 if results and all(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
