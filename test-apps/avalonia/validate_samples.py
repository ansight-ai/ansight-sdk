#!/usr/bin/env python3
"""Launch built children and retain baseline results, separately from full feature coverage."""

import argparse
import datetime
import json
import shutil
import subprocess
import time
from pathlib import Path


SUITE_ROOT = Path(__file__).resolve().parent
SDK_ROOT = SUITE_ROOT.parents[1]
TASKS = ["session.metadata", "avalonia.accessibility", "avalonia.inspect"]
TOOLS = ["ui.get_visual_tree", "avalonia.get_roots", "avalonia.get_rendering_diagnostics"]


def timestamp():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def command(cli, arguments, destination=None, timeout=330):
    process = subprocess.run([cli, *arguments, "--json"], capture_output=True, text=True, timeout=timeout)
    if destination:
        destination.write_text(process.stdout)
        if process.stderr:
            destination.with_suffix(".stderr.log").write_text(process.stderr)
    data = json.loads(process.stdout)
    return process.returncode, data


def connected(cli, app_id, platform):
    code, data = command(cli, ["session", "list", "--app-id", app_id, "--platform", platform, "--connected"])
    if code:
        raise RuntimeError(data.get("message", "Session discovery failed."))
    if not data["sessions"] and app_id != app_id.lower():
        code, data = command(cli, ["session", "list", "--search", app_id, "--platform", platform, "--connected"])
        if code:
            raise RuntimeError(data.get("message", "Session discovery failed."))
        return [session for session in data["sessions"] if session["appId"] == app_id]
    return data["sessions"]


def require_device_operation(cli, arguments, destination):
    code, data = command(cli, ["device", *arguments], destination)
    if code or not data.get("result", {}).get("isSuccess"):
        raise RuntimeError(data.get("result", {}).get("message", data.get("message", "Device operation failed.")))


def validate(cli, child, platform, device_id, build, directory):
    app_id = child["appIds"][platform]
    binaries = build.get("binaries", [])
    if len(binaries) != 1:
        raise RuntimeError(f"Expected one installable binary for {child['id']}/{platform}.")
    binary = binaries[0]
    record = {
        "schemaVersion": 1, "app": child["id"], "appId": app_id, "platform": platform,
        "deviceId": device_id, "binarySha256": binary["sha256"], "startedAtUtc": timestamp(),
        "scope": "Launch, session metadata, framework trees and retained render metrics",
        "fullFeatureValidation": "not_complete", "inputInteractionValidation": "not_run",
        "baselineStatus": "failed", "tasks": [],
    }
    if connected(cli, app_id, platform):
        record.update(baselineStatus="blocked", error="An existing live app instance was preserved.")
        return record

    launched = False
    session_id = None
    try:
        require_device_operation(cli, ["install", platform, device_id, binary["path"]], directory / "install.json")
        # Cleanup still applies when launch reports an error after starting the process.
        launched = True
        require_device_operation(cli, ["launch", platform, device_id, app_id], directory / "launch.json")
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            sessions = connected(cli, app_id, platform)
            if len(sessions) > 1:
                raise RuntimeError("More than one new matching session appeared; refusing ambiguous validation.")
            if sessions:
                session_id = sessions[0]["sessionId"]
                (directory / "connected-session.json").write_text(json.dumps(sessions[0], indent=2) + "\n")
                break
            time.sleep(1)
        if not session_id:
            raise RuntimeError("The launched app did not establish an SDK session within 45 seconds.")
        record["sessionId"] = session_id

        for tool_id in TOOLS:
            code, catalog = command(cli, ["app", "tools", session_id, "--tool-id", tool_id, "--detail", "full"],
                                    directory / f"{tool_id}.schema.json")
            definitions = catalog.get("envelope", {}).get("payload", {}).get("tools", [])
            if code or not any(tool["id"] == tool_id and tool.get("executable") for tool in definitions):
                raise RuntimeError(f"Required tool {tool_id} is unavailable.")

        for task_id in TASKS:
            task_arguments = ["task", "run", task_id, "--app-id", app_id,
                              "--session-id", session_id, "--repository", str(SUITE_ROOT)]
            if task_id == "avalonia.inspect":
                task_arguments += ["--input", '{"requireInputMetrics":false}']
            code, data = command(cli, task_arguments,
                                 directory / f"{task_id}.json")
            result = data.get("result", {})
            assertions = result.get("assertions", [])
            passed = (
                code == 0
                and result.get("status") == 0
                and bool(assertions)
                and all(item["passed"] for item in assertions)
            )
            record["tasks"].append({
                "taskId": task_id, "runId": result.get("runId"), "passed": passed,
                "message": result.get("message", data.get("message")),
                "assertionCount": len(assertions),
                "failedAssertions": [item["assertionId"] for item in assertions if not item["passed"]],
            })

        # Preserve native accessibility independently of framework peer capture.
        code, _ = command(cli, ["ui", "snapshot", "--session", session_id, "--detail", "full",
                               "--max-nodes", "2000", "--max-depth", "24"], directory / "device-accessibility.json")
        record["nativeAccessibilityCaptureSucceeded"] = code == 0
        command(cli, ["session", "screenshot", "export", session_id, "--output", str(directory / "final.png")],
                directory / "screenshot-export.json")
        record["baselineStatus"] = "passed" if all(task["passed"] for task in record["tasks"]) else "failed"
    except (RuntimeError, subprocess.TimeoutExpired, ValueError, OSError) as exception:
        record["error"] = str(exception)
    finally:
        if launched:
            try:
                require_device_operation(cli, ["terminate", platform, device_id, app_id], directory / "terminate.json")
                deadline = time.monotonic() + 15
                sessions = connected(cli, app_id, platform)
                while sessions and time.monotonic() < deadline:
                    time.sleep(1)
                    sessions = connected(cli, app_id, platform)
                record["sessionEnded"] = not sessions
                if sessions:
                    record["baselineStatus"] = "failed"
            except Exception as exception:
                record.update(cleanupError=str(exception), baselineStatus="failed")
        record["finishedAtUtc"] = timestamp()
    return record


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=SDK_ROOT / ".ansight-validation/avalonia")
    parser.add_argument("--app", action="append")
    parser.add_argument("--platform", action="append", choices=["ios", "android"])
    parser.add_argument("--ios-device", required=True)
    parser.add_argument("--android-device", required=True)
    parser.add_argument("--watch-seconds", type=int, default=0)
    parser.add_argument("--cli", default=shutil.which("ansight"))
    arguments = parser.parse_args()
    suite = json.loads((SUITE_ROOT / "suite.json").read_text())
    children = [child for child in suite["children"] if not arguments.app or child["id"] in arguments.app]
    if not arguments.cli or not children:
        parser.error("The CLI and at least one suite child are required.")
    missing = set(arguments.app or []) - {child["id"] for child in children}
    if missing:
        parser.error(f"Unknown child IDs: {', '.join(sorted(missing))}")
    devices = {"ios": arguments.ios_device, "android": arguments.android_device}
    deadline = time.monotonic() + arguments.watch_seconds
    attempted = set()
    while True:
        for child in children:
            for platform in arguments.platform or ["ios", "android"]:
                directory = arguments.output.resolve() / "results" / child["id"] / platform
                build_path = directory / "build.json"
                if not build_path.exists():
                    continue
                build = json.loads(build_path.read_text())
                if build.get("buildStatus") != "passed":
                    continue
                key = (child["id"], platform, build["startedAtUtc"])
                if key in attempted:
                    continue
                attempted.add(key)
                receipt_path = directory / "baseline.json"
                if receipt_path.exists():
                    old = json.loads(receipt_path.read_text())
                    if old.get("buildStartedAtUtc") == build["startedAtUtc"]:
                        continue
                    archive = directory / "baseline-history" / old["startedAtUtc"].replace(":", "-")
                    archive.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(receipt_path, archive / "baseline.json")
                record = validate(arguments.cli, child, platform, devices[platform], build, directory)
                record["buildStartedAtUtc"] = build["startedAtUtc"]
                receipt_path.write_text(json.dumps(record, indent=2) + "\n")
                print(json.dumps(record), flush=True)
        if time.monotonic() >= deadline:
            break
        time.sleep(5)


if __name__ == "__main__":
    main()
