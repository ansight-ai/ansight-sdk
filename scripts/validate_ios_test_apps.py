#!/usr/bin/env python3
"""Prepare and run native iOS test apps with the local Ansight Swift SDK.

The script intentionally writes its generated integration files into each test
app checkout. It also rewrites project.pbxproj as XML plist because Xcode accepts
that format and it lets us mutate the project as structured data.
"""

from __future__ import annotations

import argparse
import dataclasses
import datetime as dt
import hashlib
import json
import os
import plistlib
import re
import select
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import zlib
from pathlib import Path
from typing import Any


DEFAULT_TEST_APPS_ROOT = Path("/Users/matthewrobbins/Development/git/ansight-sdk-test-apps/ios")
DEFAULT_SDK_PACKAGE = Path("/Users/matthewrobbins/Development/git/ansight-sdk/src/ios")
DEFAULT_OUTPUT_ROOT = Path("/Users/matthewrobbins/Development/git/ansight-sdk/.ansight-validation")
DEFAULT_STUDIO_DAEMON = Path("/Applications/Ansight.app/Contents/Helpers/ansight-daemon")
BUILD_SETTINGS_TIMEOUT_SECONDS = 120
BUILD_TIMEOUT_SECONDS = 900
EXCLUDED_DISCOVERY_PARTS = {
    ".ansight-validation",
    ".build",
    ".git",
    "Build",
    "Carthage",
    "DerivedData",
    "Pods",
    "SourcePackages",
}

APP_PRODUCT_TYPE = "com.apple.product-type.application"
PACKAGE_PRODUCT_NAME = "Ansight"
VALIDATION_GROUP_NAME = "AnsightValidation"
VALIDATION_SWIFT_FILE = "AnsightValidationBootstrap.swift"
VALIDATION_OBJC_FILE = "AnsightValidationConstructor.m"
VALIDATION_ASSETS_DIR = "AnsightValidationAssets.xcassets"
VALIDATION_APP_ICON_NAME = "AnsightValidationAppIcon"
VALIDATION_APP_ICON_SET = f"{VALIDATION_APP_ICON_NAME}.appiconset"
VALIDATION_ROUTE_NAME = "Ansight SDK Validation Route"
VALIDATION_BINARY_FILE = "large-transfer.bin"
VALIDATION_BINARY_SIZE_BYTES = 150_000
VALIDATION_BINARY_CHUNK_BYTES = 64 * 1024


@dataclasses.dataclass(frozen=True)
class CommandResult:
    command: list[str]
    cwd: Path | None
    returncode: int
    stdout: str
    stderr: str


@dataclasses.dataclass
class AppProject:
    slug: str
    root: Path
    project_path: Path
    workspace_path: Path | None
    scheme: str | None = None
    target_name: str | None = None
    bundle_id: str | None = None
    app_name: str | None = None
    pairing_config_id: str | None = None


@dataclasses.dataclass
class ValidationResult:
    slug: str
    project: str
    scheme: str | None
    bundle_id: str | None
    app_name: str | None = None
    pairing_config_id: str | None = None
    prepared: bool = False
    built: bool = False
    installed: bool = False
    launched: bool = False
    launched_at_utc: str | None = None
    studio_verified: bool = False
    studio_session_id: str | None = None
    studio_status: str | None = None
    studio_metric_sample_count: int | None = None
    studio_fps_sample_count: int | None = None
    studio_image_count: int | None = None
    studio_tool_count: int | None = None
    studio_icon_image_path: str | None = None
    studio_icon_synced: bool = False
    studio_session_icon_synced: bool = False
    studio_session_icon_width: int | None = None
    studio_session_icon_height: int | None = None
    studio_session_icon_byte_count: int | None = None
    studio_device_profile_details_synced: bool = False
    studio_device_profile_runtime_code: int | None = None
    studio_device_profile_network_transport_code: int | None = None
    studio_device_profile_gpu_api_code: int | None = None
    studio_device_profile_render_backend_code: int | None = None
    studio_device_profile_environment_code: int | None = None
    studio_device_profile_privacy_safe: bool = False
    validation_app_icon_injected: bool = False
    validation_route_resolver_injected: bool = False
    studio_validation_route_seen: bool = False
    studio_binary_download_metadata_verified: bool = False
    studio_binary_download_reassembled: bool = False
    studio_binary_download_size_bytes: int | None = None
    studio_binary_download_received_bytes: int | None = None
    studio_binary_download_transfer_id: str | None = None
    studio_binary_download_artifact_path: str | None = None
    studio_binary_download_error: str | None = None
    pod_install_attempted: bool = False
    pod_install_succeeded: bool = False
    studio_error: str | None = None
    status: str = "pending"
    failure_stage: str | None = None
    error_summary: str | None = None
    error: str | None = None
    app_path: str | None = None


class StudioMCPClient:
    def __init__(self, daemon_path: Path, request_timeout_seconds: int = 15) -> None:
        self.daemon_path = daemon_path
        self.request_timeout_seconds = request_timeout_seconds
        self.process: subprocess.Popen[str] | None = None
        self.next_request_id = 0

    def __enter__(self) -> "StudioMCPClient":
        self.start()
        return self

    def __exit__(self, exc_type: object, exc: object, tb: object) -> None:
        self.close()

    def start(self) -> None:
        if self.process is not None:
            return

        self.process = subprocess.Popen(
            [str(self.daemon_path), "mcp-stdio"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        self.request(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {
                    "name": "ansight-ios-test-app-validator",
                    "version": "0.1",
                },
            },
        )
        self.notify("notifications/initialized", {})

    def close(self) -> None:
        if self.process is None:
            return
        self.process.terminate()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.kill()
        self.process = None

    def notify(self, method: str, params: dict[str, Any]) -> None:
        process = self.require_process()
        assert process.stdin is not None
        process.stdin.write(json.dumps({"jsonrpc": "2.0", "method": method, "params": params}) + "\n")
        process.stdin.flush()

    def request(self, method: str, params: dict[str, Any]) -> dict[str, Any]:
        process = self.require_process()
        assert process.stdin is not None
        assert process.stdout is not None

        self.next_request_id += 1
        request_id = self.next_request_id
        process.stdin.write(
            json.dumps(
                {
                    "jsonrpc": "2.0",
                    "id": request_id,
                    "method": method,
                    "params": params,
                }
            )
            + "\n"
        )
        process.stdin.flush()

        deadline = time.monotonic() + self.request_timeout_seconds
        while time.monotonic() < deadline:
            remaining = max(0.0, deadline - time.monotonic())
            readable, _, _ = select.select([process.stdout], [], [], remaining)
            if not readable:
                break
            line = process.stdout.readline()
            if not line:
                break
            message = json.loads(line)
            if message.get("id") != request_id:
                continue
            if "error" in message:
                raise RuntimeError(message["error"])
            return message.get("result", {})

        raise RuntimeError(f"Timed out waiting for Ansight Studio MCP response to {method}.")

    def call_tool(self, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
        result = self.request(
            "tools/call",
            {
                "name": name,
                "arguments": arguments,
            },
        )
        if result.get("isError"):
            text = "\n".join(
                item.get("text", "")
                for item in result.get("content", [])
                if isinstance(item, dict)
            ).strip()
            raise RuntimeError(text or f"Ansight Studio tool {name} failed.")
        structured = result.get("structuredContent")
        if isinstance(structured, dict):
            return structured
        return result

    def require_process(self) -> subprocess.Popen[str]:
        if self.process is None:
            raise RuntimeError("Ansight Studio MCP client is not started.")
        return self.process


def run(
    command: list[str],
    cwd: Path | None = None,
    env: dict[str, str] | None = None,
    check: bool = False,
    timeout: int | None = None,
) -> CommandResult:
    merged_env = os.environ.copy()
    if env:
        merged_env.update(env)
    process = subprocess.run(
        command,
        cwd=str(cwd) if cwd else None,
        env=merged_env,
        text=True,
        capture_output=True,
        timeout=timeout,
    )
    result = CommandResult(
        command=command,
        cwd=cwd,
        returncode=process.returncode,
        stdout=process.stdout,
        stderr=process.stderr,
    )
    if check and result.returncode != 0:
        raise RuntimeError(format_command_failure(result))
    return result


def format_command_failure(result: CommandResult, max_chars: int = 4000) -> str:
    location = f" (cwd={result.cwd})" if result.cwd else ""
    text = f"{' '.join(result.command)}{location} exited {result.returncode}"
    combined = "\n".join(part for part in [result.stdout, result.stderr] if part)
    if combined:
        text += "\n" + combined[-max_chars:]
    return text


def summarize_error(error: Exception | str, max_chars: int = 600) -> str:
    text = str(error)
    preferred_patterns = [
        r"error: [^\n]+",
        r"ld: [^\n]+",
        r"\*\* BUILD FAILED \*\*",
        r"Timed out waiting[^\n]+",
        r"No live Studio session[^\n]+",
        r"No pairing config[^\n]+",
    ]
    matches: list[str] = []
    for pattern in preferred_patterns:
        matches.extend(re.findall(pattern, text))
    summary = " | ".join(dict.fromkeys(matches)) if matches else text.splitlines()[0] if text else ""
    if len(summary) > max_chars:
        return summary[: max_chars - 3] + "..."
    return summary


def slugify(value: str) -> str:
    slug = re.sub(r"[^A-Za-z0-9_.-]+", "-", value.strip()).strip("-")
    return slug or "app"


def deterministic_id(namespace: str, existing: dict[str, Any]) -> str:
    seed = namespace
    while True:
        candidate = hashlib.sha1(seed.encode("utf-8")).hexdigest()[:24].upper()
        if candidate not in existing:
            return candidate
        seed += ":"


def discover_projects(root: Path) -> list[AppProject]:
    projects: list[AppProject] = []
    for project_path in sorted(root.rglob("*.xcodeproj")):
        try:
            relative_project = project_path.relative_to(root)
            if any(part in EXCLUDED_DISCOVERY_PARTS for part in relative_project.parts):
                continue
        except ValueError:
            pass

        if project_path.name == "Pods.xcodeproj" or "/Pods/" in str(project_path):
            continue
        app_root = project_path.parent
        try:
            relative_root = app_root.relative_to(root)
            slug = slugify("__".join(relative_root.parts))
        except ValueError:
            slug = slugify(app_root.name)
        workspaces = sorted(
            workspace
            for workspace in app_root.glob("*.xcworkspace")
            if workspace.name != "Pods.xcworkspace"
        )
        projects.append(
            AppProject(
                slug=slug,
                root=app_root,
                project_path=project_path,
                workspace_path=workspaces[0] if workspaces else None,
            )
        )
    return projects


def load_project(project_path: Path) -> dict[str, Any]:
    pbxproj_path = project_path / "project.pbxproj"
    with tempfile.NamedTemporaryFile(suffix=".plist", delete=False) as temporary:
        temporary_path = Path(temporary.name)
    try:
        run(["plutil", "-convert", "xml1", "-o", str(temporary_path), str(pbxproj_path)], check=True)
        with temporary_path.open("rb") as file:
            return plistlib.load(file)
    finally:
        temporary_path.unlink(missing_ok=True)


def save_project(project_path: Path, project: dict[str, Any]) -> None:
    pbxproj_path = project_path / "project.pbxproj"
    backup_path = project_path / "project.pbxproj.ansight-validation-backup"
    if not backup_path.exists():
        shutil.copy2(pbxproj_path, backup_path)
    with pbxproj_path.open("wb") as file:
        plistlib.dump(project, file, sort_keys=False)


def object_map(project: dict[str, Any]) -> dict[str, dict[str, Any]]:
    objects = project.get("objects")
    if not isinstance(objects, dict):
        raise RuntimeError("project.pbxproj does not contain an objects dictionary.")
    return objects


def find_app_targets(objects: dict[str, dict[str, Any]]) -> list[tuple[str, dict[str, Any]]]:
    return [
        (object_id, value)
        for object_id, value in objects.items()
        if value.get("isa") == "PBXNativeTarget" and value.get("productType") == APP_PRODUCT_TYPE
    ]


def select_app_target(
    objects: dict[str, dict[str, Any]],
    preferred_scheme: str | None,
    preferred_target: str | None,
) -> tuple[str, dict[str, Any]]:
    app_targets = find_app_targets(objects)
    if not app_targets:
        raise RuntimeError("No application target found in project.")
    for requested in [preferred_target, preferred_scheme]:
        if requested:
            for object_id, target in app_targets:
                if target.get("name") == requested:
                    return object_id, target
    return app_targets[0]


def find_phase(
    objects: dict[str, dict[str, Any]],
    target: dict[str, Any],
    isa: str,
) -> tuple[str, dict[str, Any]]:
    for phase_id in target.get("buildPhases", []):
        phase = objects.get(phase_id)
        if phase and phase.get("isa") == isa:
            return phase_id, phase
    raise RuntimeError(f"Target {target.get('name')} does not contain a {isa}.")


def find_project_object(objects: dict[str, dict[str, Any]]) -> tuple[str, dict[str, Any]]:
    for object_id, value in objects.items():
        if value.get("isa") == "PBXProject":
            return object_id, value
    raise RuntimeError("PBXProject object not found.")


def find_build_configurations(
    objects: dict[str, dict[str, Any]],
    target: dict[str, Any],
) -> list[dict[str, Any]]:
    configuration_list_id = target.get("buildConfigurationList")
    configuration_list = objects.get(configuration_list_id, {})
    configurations: list[dict[str, Any]] = []
    for configuration_id in configuration_list.get("buildConfigurations", []):
        configuration = objects.get(configuration_id)
        if configuration and configuration.get("isa") == "XCBuildConfiguration":
            configurations.append(configuration)
    return configurations


def ensure_project_group(
    objects: dict[str, dict[str, Any]],
    project_object: dict[str, Any],
    namespace: str,
) -> str:
    main_group_id = project_object.get("mainGroup")
    main_group = objects.get(main_group_id)
    if not main_group:
        raise RuntimeError("Project main group not found.")

    for child_id in main_group.get("children", []):
        child = objects.get(child_id)
        if child and child.get("isa") == "PBXGroup" and child.get("path") == VALIDATION_GROUP_NAME:
            return child_id

    group_id = deterministic_id(namespace + ":group", objects)
    objects[group_id] = {
        "isa": "PBXGroup",
        "children": [],
        "path": VALIDATION_GROUP_NAME,
        "sourceTree": "<group>",
    }
    main_group.setdefault("children", []).append(group_id)
    return group_id


def ensure_file_reference(
    objects: dict[str, dict[str, Any]],
    group: dict[str, Any],
    namespace: str,
    filename: str,
    file_type: str,
) -> str:
    for child_id in group.get("children", []):
        child = objects.get(child_id)
        if child and child.get("isa") == "PBXFileReference" and child.get("path") == filename:
            child["lastKnownFileType"] = file_type
            return child_id

    file_id = deterministic_id(namespace + ":file:" + filename, objects)
    objects[file_id] = {
        "isa": "PBXFileReference",
        "lastKnownFileType": file_type,
        "path": filename,
        "sourceTree": "<group>",
    }
    group.setdefault("children", []).append(file_id)
    return file_id


def ensure_source_build_file(
    objects: dict[str, dict[str, Any]],
    sources_phase: dict[str, Any],
    namespace: str,
    file_ref_id: str,
) -> str:
    for build_file_id in sources_phase.get("files", []):
        build_file = objects.get(build_file_id)
        if build_file and build_file.get("fileRef") == file_ref_id:
            return build_file_id

    build_file_id = deterministic_id(namespace + ":source-build:" + file_ref_id, objects)
    objects[build_file_id] = {
        "isa": "PBXBuildFile",
        "fileRef": file_ref_id,
    }
    sources_phase.setdefault("files", []).append(build_file_id)
    return build_file_id


def ensure_resource_build_file(
    objects: dict[str, dict[str, Any]],
    resources_phase: dict[str, Any],
    namespace: str,
    file_ref_id: str,
) -> str:
    for build_file_id in resources_phase.get("files", []):
        build_file = objects.get(build_file_id)
        if build_file and build_file.get("fileRef") == file_ref_id:
            return build_file_id

    build_file_id = deterministic_id(namespace + ":resource-build:" + file_ref_id, objects)
    objects[build_file_id] = {
        "isa": "PBXBuildFile",
        "fileRef": file_ref_id,
    }
    resources_phase.setdefault("files", []).append(build_file_id)
    return build_file_id


def ensure_local_package_reference(
    objects: dict[str, dict[str, Any]],
    project_object: dict[str, Any],
    sdk_package: Path,
    project_root: Path,
    namespace: str,
) -> str:
    relative_path = os.path.relpath(sdk_package, project_root)
    for package_id in project_object.get("packageReferences", []):
        package = objects.get(package_id)
        if package and package.get("isa") == "XCLocalSwiftPackageReference":
            if package.get("relativePath") == relative_path or Path(project_root, package.get("relativePath", "")).resolve() == sdk_package.resolve():
                package["relativePath"] = relative_path
                return package_id

    package_id = deterministic_id(namespace + ":local-package:" + str(sdk_package), objects)
    objects[package_id] = {
        "isa": "XCLocalSwiftPackageReference",
        "relativePath": relative_path,
    }
    project_object.setdefault("packageReferences", []).append(package_id)
    return package_id


def ensure_package_product_dependency(
    objects: dict[str, dict[str, Any]],
    target: dict[str, Any],
    package_ref_id: str,
    namespace: str,
) -> str:
    for dependency_id in target.get("packageProductDependencies", []):
        dependency = objects.get(dependency_id)
        if (
            dependency
            and dependency.get("isa") == "XCSwiftPackageProductDependency"
            and dependency.get("productName") == PACKAGE_PRODUCT_NAME
        ):
            dependency["package"] = package_ref_id
            return dependency_id

    dependency_id = deterministic_id(namespace + ":product:" + PACKAGE_PRODUCT_NAME, objects)
    objects[dependency_id] = {
        "isa": "XCSwiftPackageProductDependency",
        "package": package_ref_id,
        "productName": PACKAGE_PRODUCT_NAME,
    }
    target.setdefault("packageProductDependencies", []).append(dependency_id)
    return dependency_id


def ensure_framework_build_file(
    objects: dict[str, dict[str, Any]],
    frameworks_phase: dict[str, Any],
    product_dependency_id: str,
    namespace: str,
) -> str:
    for build_file_id in frameworks_phase.get("files", []):
        build_file = objects.get(build_file_id)
        if build_file and build_file.get("productRef") == product_dependency_id:
            return build_file_id

    build_file_id = deterministic_id(namespace + ":framework-build:" + product_dependency_id, objects)
    objects[build_file_id] = {
        "isa": "PBXBuildFile",
        "productRef": product_dependency_id,
    }
    frameworks_phase.setdefault("files", []).append(build_file_id)
    return build_file_id


def normalize_build_settings(
    target: dict[str, Any],
    objects: dict[str, dict[str, Any]],
    validation_app_icon_name: str | None = None,
) -> None:
    for configuration in find_build_configurations(objects, target):
        settings = configuration.setdefault("buildSettings", {})
        settings.setdefault("SWIFT_VERSION", "5.0")
        settings.setdefault("CLANG_ENABLE_MODULES", "YES")
        settings.setdefault("ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES")
        current_target = str(settings.get("IPHONEOS_DEPLOYMENT_TARGET", "")).strip()
        if not current_target or version_tuple(current_target) < (15, 0):
            settings["IPHONEOS_DEPLOYMENT_TARGET"] = "15.0"
        if validation_app_icon_name:
            settings["ASSETCATALOG_COMPILER_APPICON_NAME"] = validation_app_icon_name


def version_tuple(value: str) -> tuple[int, int]:
    parts = re.findall(r"\d+", value)
    if not parts:
        return (0, 0)
    first = int(parts[0])
    second = int(parts[1]) if len(parts) > 1 else 0
    return first, second


def find_pairing_config(
    pairing_config_dir: Path,
    bundle_id: str,
    explicit_path: Path | None,
    host_address: str,
    discovery_port: int,
) -> str:
    if explicit_path:
        return normalize_pairing_config(
            explicit_path.read_text(encoding="utf-8"),
            host_address,
            discovery_port,
        )

    candidates = [
        pairing_config_dir / f"{bundle_id}.json",
        pairing_config_dir / f"{bundle_id.lower()}.json",
        pairing_config_dir / f"{slugify(bundle_id)}.json",
        pairing_config_dir / f"{bundle_id.lower()}.ans.json",
        pairing_config_dir / f"{slugify(bundle_id)}.ans.json",
    ]
    for candidate in candidates:
        if candidate.exists():
            return normalize_pairing_config(
                candidate.read_text(encoding="utf-8"),
                host_address,
                discovery_port,
            )

    for candidate in sorted(pairing_config_dir.glob("*.json")) + sorted(pairing_config_dir.glob("*.ans.json")):
        try:
            config = json.loads(candidate.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        if config.get("appId") == bundle_id:
            return normalize_pairing_config(
                json.dumps(config, indent=2),
                host_address,
                discovery_port,
            )

    raise RuntimeError(
        f"No pairing config for {bundle_id} in {pairing_config_dir}. "
        f"Issue one from Ansight Studio and save it as {bundle_id}.json."
    )


def normalize_pairing_config(raw_json: str, host_address: str, discovery_port: int) -> str:
    config = json.loads(raw_json)
    schema = config.get("schema")
    discovery = {
        "schema": "ansight.discovery-hint.v1",
        "source": "ios-test-app-validator",
        "hostAddresses": [host_address],
        "discoveryPort": discovery_port,
    }

    if schema == "ansight.pairing-config.v1":
        return json.dumps(
            {
                "schema": "ansight.pairing-config-document.v1",
                "config": config,
                "discovery": discovery,
            },
            indent=2,
        )

    if schema in {"ansight.pairing-config-document.v1", "ansight.pairing-ticket.v1"}:
        if not config.get("discovery"):
            config["discovery"] = discovery
        else:
            config["discovery"].setdefault("schema", "ansight.discovery-hint.v1")
            config["discovery"].setdefault("source", "ios-test-app-validator")
            config["discovery"].setdefault("hostAddresses", [host_address])
            config["discovery"].setdefault("discoveryPort", discovery_port)
        return json.dumps(config, indent=2)

    raise RuntimeError(f"Unsupported pairing config schema: {schema!r}")


def swift_string_literal(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def write_validation_sources(
    project_root: Path,
    app_name: str,
    bundle_id: str,
    pairing_config_json: str,
    inject_validation_route_resolver: bool,
) -> None:
    validation_dir = project_root / VALIDATION_GROUP_NAME
    validation_dir.mkdir(parents=True, exist_ok=True)
    swift_file = validation_dir / VALIDATION_SWIFT_FILE
    objc_file = validation_dir / VALIDATION_OBJC_FILE
    validation_route_resolver = ""
    if inject_validation_route_resolver:
        validation_route_resolver = f"""
            runtime.setScreenRouteResolver(AnsightScreenRouteResolver {{ context in
                AnsightScreenRoute(
                    name: {swift_string_literal(VALIDATION_ROUTE_NAME)},
                    key: "ansight-validation-route:\\(expectedBundleId)",
                    details: [
                        "route": "/ansight-validation",
                        "defaultScreen": context.defaultName,
                        "screenSource": context.source,
                        "viewController": context.viewControllerName,
                        "swiftUIRoot": context.swiftUIRootTypeName ?? ""
                    ]
                )
            }})
"""

    swift_file.write_text(
        f"""import Ansight
import Foundation
import UIKit

@_cdecl("ansight_validation_bootstrap")
public func ansight_validation_bootstrap() {{
    AnsightValidationBootstrap.start()
}}

private enum AnsightValidationBootstrap {{
    private static let lock = NSLock()
    private static var didStart = false
    private static let appName = {swift_string_literal(app_name)}
    private static let expectedBundleId = {swift_string_literal(bundle_id)}
    private static let pairingConfigJson = ###\"\"\"
{pairing_config_json.rstrip()}
\"\"\"###

    static func start() {{
        lock.lock()
        let shouldStart = !didStart
        didStart = true
        lock.unlock()

        guard shouldStart else {{
            return
        }}

        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {{
            run()
        }}
    }}

    private static func run() {{
        seedValidationArtifacts()

        let runtime = AnsightRuntime.shared
        do {{
{validation_route_resolver.rstrip()}
            var options = AnsightOptions.ansightDeveloperDefaults
            options.hostConnection = AnsightHostConnectionOptions(
                savedConfigKey: "ai.ansight.validation.\\(expectedBundleId)",
                bundledConfigJson: pairingConfigJson
            )
            options.hostAutoProbe = .disabledDefault
            options.customProperties = [
                "ansightValidation": [
                    "appName": appName,
                    "expectedBundleId": expectedBundleId,
                    "actualBundleId": Bundle.main.bundleIdentifier ?? ""
                ]
            ]

            try runtime.initializeAndActivateAnsightSdk(options: options)
            try runtime.screenViewed(
                "Ansight SDK Validation",
                details: [
                    "app": appName,
                    "bundleId": Bundle.main.bundleIdentifier ?? ""
                ]
            )
            runtime.setAppLifecycleState(.foreground)
            try runtime.event(
                "ansight.validation.bootstrap",
                details: "SDK validation bootstrap started."
            )
            try runtime.metric(Int64(Date().timeIntervalSince1970 * 1000) % 10_000)
        }} catch {{
            NSLog("[AnsightValidation] initialization failed: \\(error)")
            return
        }}

        Task {{
            let result = await runtime.connect(.bundledConfig(clientName: "Ansight SDK Validation - \\(appName)"))
            if result.success {{
                NSLog("[AnsightValidation] connected: \\(result.message)")
            }} else {{
                NSLog("[AnsightValidation] connect failed: \\(result.message)")
            }}
        }}
    }}

    private static func seedValidationArtifacts() {{
        UserDefaults.standard.set("started", forKey: "ansight.validation.status")
        UserDefaults.standard.set(appName, forKey: "ansight.validation.appName")
        UserDefaults.standard.set(expectedBundleId, forKey: "ansight.validation.expectedBundleId")
        UserDefaults.standard.synchronize()

        guard let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first else {{
            return
        }}

        let directory = documents.appendingPathComponent("ansight-validation", isDirectory: true)
        do {{
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let file = directory.appendingPathComponent("validation.txt")
            let body = "Ansight SDK validation for \\(appName) [\\(expectedBundleId)]"
            try body.write(to: file, atomically: true, encoding: .utf8)

            let binaryFile = directory.appendingPathComponent("{VALIDATION_BINARY_FILE}")
            var binaryPayload = Data()
            binaryPayload.reserveCapacity({VALIDATION_BINARY_SIZE_BYTES})
            for index in 0..<{VALIDATION_BINARY_SIZE_BYTES} {{
                binaryPayload.append(UInt8(index % 251))
            }}
            try binaryPayload.write(to: binaryFile, options: .atomic)
        }} catch {{
            NSLog("[AnsightValidation] artifact seed failed: \\(error)")
        }}
    }}
}}
""",
        encoding="utf-8",
    )

    objc_file.write_text(
        """#import <Foundation/Foundation.h>

extern void ansight_validation_bootstrap(void);

__attribute__((constructor))
static void ansight_validation_constructor(void) {
    ansight_validation_bootstrap();
}
""",
        encoding="utf-8",
    )


def write_validation_app_icon_assets(project_root: Path) -> None:
    assets_dir = project_root / VALIDATION_GROUP_NAME / VALIDATION_ASSETS_DIR
    app_icon_dir = assets_dir / VALIDATION_APP_ICON_SET
    app_icon_dir.mkdir(parents=True, exist_ok=True)

    (assets_dir / "Contents.json").write_text(
        json.dumps({"info": {"author": "ansight", "version": 1}}, indent=2) + "\n",
        encoding="utf-8",
    )

    entries = validation_app_icon_entries()
    images: list[dict[str, str]] = []
    for index, entry in enumerate(entries):
        dimension = icon_dimension_pixels(entry["size"], entry["scale"])
        size_label = entry["size"].replace(".", "_").replace("x", "x")
        filename = f"ansight-validation-icon-{entry['idiom']}-{size_label}-{entry['scale']}.png"
        (app_icon_dir / filename).write_bytes(make_validation_png(dimension, index))
        images.append(
            {
                "idiom": entry["idiom"],
                "size": entry["size"],
                "scale": entry["scale"],
                "filename": filename,
            }
        )

    (app_icon_dir / "Contents.json").write_text(
        json.dumps({"images": images, "info": {"author": "ansight", "version": 1}}, indent=2) + "\n",
        encoding="utf-8",
    )


def validation_app_icon_entries() -> list[dict[str, str]]:
    return [
        {"idiom": "iphone", "size": "20x20", "scale": "2x"},
        {"idiom": "iphone", "size": "20x20", "scale": "3x"},
        {"idiom": "iphone", "size": "29x29", "scale": "2x"},
        {"idiom": "iphone", "size": "29x29", "scale": "3x"},
        {"idiom": "iphone", "size": "40x40", "scale": "2x"},
        {"idiom": "iphone", "size": "40x40", "scale": "3x"},
        {"idiom": "iphone", "size": "60x60", "scale": "2x"},
        {"idiom": "iphone", "size": "60x60", "scale": "3x"},
        {"idiom": "ipad", "size": "20x20", "scale": "1x"},
        {"idiom": "ipad", "size": "20x20", "scale": "2x"},
        {"idiom": "ipad", "size": "29x29", "scale": "1x"},
        {"idiom": "ipad", "size": "29x29", "scale": "2x"},
        {"idiom": "ipad", "size": "40x40", "scale": "1x"},
        {"idiom": "ipad", "size": "40x40", "scale": "2x"},
        {"idiom": "ipad", "size": "76x76", "scale": "1x"},
        {"idiom": "ipad", "size": "76x76", "scale": "2x"},
        {"idiom": "ipad", "size": "83.5x83.5", "scale": "2x"},
        {"idiom": "ios-marketing", "size": "1024x1024", "scale": "1x"},
    ]


def icon_dimension_pixels(size: str, scale: str) -> int:
    width = float(size.split("x", maxsplit=1)[0])
    multiplier = int(scale.removesuffix("x"))
    return int(round(width * multiplier))


def make_validation_png(size: int, seed: int) -> bytes:
    def chunk(kind: bytes, payload: bytes) -> bytes:
        checksum = zlib.crc32(kind + payload) & 0xFFFFFFFF
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", checksum)

    background = (
        (32 + seed * 23) % 180,
        (92 + seed * 37) % 180,
        (160 + seed * 19) % 180,
    )
    accent = (
        (230 + seed * 11) % 256,
        (245 + seed * 7) % 256,
        (80 + seed * 13) % 256,
    )
    raw_rows: list[bytes] = []
    border = max(1, size // 12)
    stripe = max(1, size // 6)
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            if x < border or y < border or x >= size - border or y >= size - border:
                color = accent
            elif (x + y + seed * 3) % (stripe * 2) < stripe:
                color = background
            else:
                color = (
                    min(255, background[0] + 28),
                    min(255, background[1] + 28),
                    min(255, background[2] + 28),
                )
            row.extend(color)
        raw_rows.append(bytes(row))

    header = struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)
    payload = zlib.compress(b"".join(raw_rows), level=9)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", payload) + chunk(b"IEND", b"")


def xcode_list(project: AppProject, use_workspace: bool = False) -> dict[str, Any]:
    command = ["xcodebuild", "-list", "-json"]
    command += xcode_container_args(project, use_workspace=use_workspace)
    result = run(command, cwd=project.root, check=True, timeout=90)
    return json.loads(result.stdout)


def infer_scheme(project: AppProject, target_name: str) -> str | None:
    for use_workspace in [False, True]:
        if use_workspace and not project.workspace_path:
            continue
        try:
            listing = xcode_list(project, use_workspace=use_workspace)
        except Exception:
            continue
        container = listing.get("workspace" if use_workspace else "project", {})
        schemes = container.get("schemes", [])
        if target_name in schemes:
            return target_name
        if schemes:
            return schemes[0]
    return target_name


def xcode_container_args(project: AppProject, use_workspace: bool | None = None) -> list[str]:
    should_use_workspace = project.workspace_path is not None if use_workspace is None else use_workspace
    if should_use_workspace and project.workspace_path:
        return ["-workspace", str(project.workspace_path)]
    return ["-project", str(project.project_path)]


def xcode_build_container_args(project: AppProject) -> list[str]:
    return xcode_container_args(project, use_workspace=None)


def legacy_infer_scheme(project: AppProject, target_name: str) -> str | None:
    try:
        listing = xcode_list(project, use_workspace=False)
    except Exception:
        listing = {}
    schemes = listing.get("project", {}).get("schemes", [])
    if target_name in schemes:
        return target_name
    return schemes[0] if schemes else target_name


def build_settings(
    project: AppProject,
    scheme: str,
    configuration: str,
    destination_id: str | None,
    derived_data_path: Path | None,
) -> dict[str, str]:
    command = [
        "xcodebuild",
        "-showBuildSettings",
        "-json",
        *xcode_build_container_args(project),
        "-scheme",
        scheme,
        "-configuration",
        configuration,
    ]
    if destination_id:
        command += ["-destination", f"id={destination_id}"]
    if derived_data_path:
        command += ["-derivedDataPath", str(derived_data_path)]
    result = run(command, cwd=project.root, check=True, timeout=BUILD_SETTINGS_TIMEOUT_SECONDS)
    parsed = json.loads(result.stdout)
    app_entry = None
    for entry in parsed:
        settings = entry.get("buildSettings", {})
        wrapper = settings.get("WRAPPER_EXTENSION")
        if wrapper == "app":
            app_entry = settings
            break
    if app_entry is None and parsed:
        app_entry = parsed[0].get("buildSettings", {})
    return app_entry or {}


def expand_bundle_id(raw_bundle_id: str, settings: dict[str, str]) -> str:
    product_name = settings.get("PRODUCT_NAME") or settings.get("TARGET_NAME") or ""
    target_name = settings.get("TARGET_NAME") or product_name
    expanded = raw_bundle_id.replace("${PRODUCT_NAME:rfc1034identifier}", rfc1034(product_name))
    expanded = expanded.replace("$(PRODUCT_NAME:rfc1034identifier)", rfc1034(product_name))
    expanded = expanded.replace("${PRODUCT_NAME}", product_name)
    expanded = expanded.replace("$(PRODUCT_NAME)", product_name)
    expanded = expanded.replace("${TARGET_NAME}", target_name)
    expanded = expanded.replace("$(TARGET_NAME)", target_name)
    return expand_build_setting_variables(expanded, settings).strip(".")


def expand_build_setting_variables(value: str, settings: dict[str, str]) -> str:
    variable_pattern = re.compile(r"\$\(([^):}]+)(?::(rfc1034identifier))?\)|\$\{([^):}]+)(?::(rfc1034identifier))?\}")
    expanded = value
    for _ in range(8):
        changed = False

        def replace(match: re.Match[str]) -> str:
            nonlocal changed
            name = match.group(1) or match.group(3) or ""
            modifier = match.group(2) or match.group(4)
            replacement = str(settings.get(name, ""))
            if replacement == match.group(0):
                replacement = ""
            if modifier == "rfc1034identifier":
                replacement = rfc1034(replacement)
            changed = changed or replacement != match.group(0)
            return replacement

        expanded = variable_pattern.sub(replace, expanded)
        if not changed:
            break
    return variable_pattern.sub("", expanded)


def rfc1034(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9-]+", "-", value).strip("-")


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def write_issued_pairing_config(
    studio: StudioMCPClient,
    app: AppProject,
    pairing_config_dir: Path,
    duration: str,
) -> Path:
    if not app.bundle_id:
        raise RuntimeError("Cannot issue Studio pairing config before resolving the bundle id.")

    issued = studio.call_tool(
        "ansight_issue_pairing_config",
        {
            "appId": app.bundle_id,
            "appName": app.app_name or app.slug,
            "duration": duration,
        },
    )
    app.pairing_config_id = issued.get("configId")

    config_json = issued.get("configJson")
    if not config_json and isinstance(issued.get("config"), dict):
        config_json = json.dumps(issued["config"], indent=2)
    if not isinstance(config_json, str) or not config_json.strip():
        raise RuntimeError("Ansight Studio did not return a pairing config JSON payload.")

    pairing_config_dir.mkdir(parents=True, exist_ok=True)
    path = pairing_config_dir / f"{app.bundle_id.lower()}.ans.json"
    path.write_text(config_json, encoding="utf-8")
    return path


def prepare_project(
    app: AppProject,
    sdk_package: Path,
    pairing_config_dir: Path,
    explicit_pairing_config: Path | None,
    host_address: str,
    discovery_port: int,
    configuration: str,
    destination_id: str | None,
    derived_data_path: Path | None,
    inject_validation_app_icon: bool,
    inject_validation_route_resolver: bool,
) -> AppProject:
    project = load_project(app.project_path)
    objects = object_map(project)
    _, project_object = find_project_object(objects)
    target_id, target = select_app_target(objects, app.scheme, app.target_name)
    target_name = target.get("name") or app.project_path.stem
    scheme = app.scheme or infer_scheme(app, target_name)
    if not scheme:
        raise RuntimeError(f"Could not infer a scheme for {app.slug}.")

    app.scheme = scheme
    app.target_name = target_name

    settings = build_settings(app, scheme, configuration, destination_id, derived_data_path)
    bundle_id = expand_bundle_id(settings.get("PRODUCT_BUNDLE_IDENTIFIER", ""), settings)
    if not bundle_id:
        raise RuntimeError(f"Could not resolve PRODUCT_BUNDLE_IDENTIFIER for {app.slug}.")
    app.bundle_id = bundle_id
    app.app_name = settings.get("FULL_PRODUCT_NAME", target_name).removesuffix(".app")

    pairing_config_json = find_pairing_config(
        pairing_config_dir,
        bundle_id,
        explicit_pairing_config,
        host_address,
        discovery_port,
    )
    write_validation_sources(
        app.root,
        app.app_name or target_name,
        bundle_id,
        pairing_config_json,
        inject_validation_route_resolver,
    )
    if inject_validation_app_icon:
        write_validation_app_icon_assets(app.root)

    namespace = f"{app.slug}:{target_id}:ansight-validation"
    sources_phase_id, sources_phase = find_phase(objects, target, "PBXSourcesBuildPhase")
    frameworks_phase_id, frameworks_phase = find_phase(objects, target, "PBXFrameworksBuildPhase")
    resources_phase: dict[str, Any] | None = None
    if inject_validation_app_icon:
        _, resources_phase = find_phase(objects, target, "PBXResourcesBuildPhase")
    _ = sources_phase_id, frameworks_phase_id

    group_id = ensure_project_group(objects, project_object, namespace)
    group = objects[group_id]
    swift_file_ref = ensure_file_reference(
        objects,
        group,
        namespace,
        VALIDATION_SWIFT_FILE,
        "sourcecode.swift",
    )
    objc_file_ref = ensure_file_reference(
        objects,
        group,
        namespace,
        VALIDATION_OBJC_FILE,
        "sourcecode.c.objc",
    )
    ensure_source_build_file(objects, sources_phase, namespace, swift_file_ref)
    ensure_source_build_file(objects, sources_phase, namespace, objc_file_ref)
    if inject_validation_app_icon and resources_phase is not None:
        assets_file_ref = ensure_file_reference(
            objects,
            group,
            namespace,
            VALIDATION_ASSETS_DIR,
            "folder.assetcatalog",
        )
        ensure_resource_build_file(objects, resources_phase, namespace, assets_file_ref)

    package_ref = ensure_local_package_reference(objects, project_object, sdk_package, app.root, namespace)
    product_dependency = ensure_package_product_dependency(objects, target, package_ref, namespace)
    ensure_framework_build_file(objects, frameworks_phase, product_dependency, namespace)
    normalize_build_settings(
        target,
        objects,
        VALIDATION_APP_ICON_NAME if inject_validation_app_icon else None,
    )

    save_project(app.project_path, project)
    return app


def resolve_project_identity(
    app: AppProject,
    configuration: str,
    destination_id: str | None,
    derived_data_path: Path | None,
) -> AppProject:
    project = load_project(app.project_path)
    objects = object_map(project)
    _, target = select_app_target(objects, app.scheme, app.target_name)
    target_name = target.get("name") or app.project_path.stem
    scheme = app.scheme or infer_scheme(app, target_name)
    if not scheme:
        raise RuntimeError(f"Could not infer a scheme for {app.slug}.")

    settings = build_settings(app, scheme, configuration, destination_id, derived_data_path)
    bundle_id = expand_bundle_id(settings.get("PRODUCT_BUNDLE_IDENTIFIER", ""), settings)
    if not bundle_id:
        raise RuntimeError(f"Could not resolve PRODUCT_BUNDLE_IDENTIFIER for {app.slug}.")

    app.scheme = scheme
    app.target_name = target_name
    app.bundle_id = bundle_id
    app.app_name = settings.get("FULL_PRODUCT_NAME", target_name).removesuffix(".app")
    return app


def resolve_project_identity_fast(app: AppProject) -> AppProject:
    project = load_project(app.project_path)
    objects = object_map(project)
    _, target = select_app_target(objects, app.scheme, app.target_name)
    target_name = target.get("name") or app.project_path.stem

    configurations = find_build_configurations(objects, target)
    configuration = next((item for item in configurations if item.get("name") == "Debug"), None)
    configuration = configuration or (configurations[0] if configurations else None)
    settings = configuration.get("buildSettings", {}) if configuration else {}
    bundle_id = expand_bundle_id(str(settings.get("PRODUCT_BUNDLE_IDENTIFIER", "")), {
        "PRODUCT_NAME": str(settings.get("PRODUCT_NAME", target_name)).replace("$(TARGET_NAME)", target_name),
        "TARGET_NAME": target_name,
    })

    app.scheme = app.scheme or target_name
    app.target_name = target_name
    app.bundle_id = bundle_id or None
    app.app_name = str(settings.get("PRODUCT_NAME", target_name)).replace("$(TARGET_NAME)", target_name)
    return app


def build_app(
    app: AppProject,
    configuration: str,
    destination_id: str,
    derived_data_path: Path,
    deployment_target: str,
    exclude_simulator_arm64: bool,
) -> None:
    command = [
        "xcodebuild",
        *xcode_build_container_args(app),
        "-scheme",
        app.scheme or app.target_name or app.project_path.stem,
        "-configuration",
        configuration,
        "-destination",
        f"id={destination_id}",
        "-derivedDataPath",
        str(derived_data_path),
        "build",
        "CODE_SIGNING_ALLOWED=NO",
        f"IPHONEOS_DEPLOYMENT_TARGET={deployment_target}",
    ]
    if exclude_simulator_arm64:
        command.append("EXCLUDED_ARCHS[sdk=iphonesimulator*]=arm64")
    env = {
        "ANSIGHT_ALLOW_REMOTE_TOOLS": "true",
        "ANSIGHT_DEVELOPER_PAIRING_ENABLED": "false",
    }
    result = run(command, cwd=app.root, env=env, timeout=BUILD_TIMEOUT_SECONDS)
    if result.returncode != 0:
        raise RuntimeError(format_command_failure(result, max_chars=8000))


def built_app_path(
    app: AppProject,
    configuration: str,
    destination_id: str,
    derived_data_path: Path,
) -> Path:
    settings = build_settings(app, app.scheme or app.target_name or app.project_path.stem, configuration, destination_id, derived_data_path)
    target_build_dir = settings.get("TARGET_BUILD_DIR")
    full_product_name = settings.get("FULL_PRODUCT_NAME")
    if not target_build_dir or not full_product_name:
        raise RuntimeError("Could not resolve TARGET_BUILD_DIR/FULL_PRODUCT_NAME after build.")
    return Path(target_build_dir) / full_product_name


def boot_simulator(destination_id: str) -> None:
    run(["xcrun", "simctl", "boot", destination_id], check=False, timeout=60)
    run(["xcrun", "simctl", "bootstatus", destination_id, "-b"], check=True, timeout=180)


def install_cocoapods_dependencies(app: AppProject, timeout_seconds: int) -> bool:
    podfile = app.root / "Podfile"
    if not podfile.exists():
        return False

    run(["pod", "install"], cwd=app.root, check=True, timeout=timeout_seconds)
    workspaces = sorted(
        workspace
        for workspace in app.root.glob("*.xcworkspace")
        if workspace.name != "Pods.xcworkspace"
    )
    if workspaces:
        app.workspace_path = workspaces[0]
    return True


def install_and_launch(app: AppProject, app_path: Path, destination_id: str) -> None:
    if not app.bundle_id:
        raise RuntimeError("App bundle id is not resolved.")
    run(["xcrun", "simctl", "install", destination_id, str(app_path)], check=True, timeout=180)
    run(["xcrun", "simctl", "terminate", destination_id, app.bundle_id], check=False, timeout=30)
    run(["xcrun", "simctl", "launch", destination_id, app.bundle_id], check=True, timeout=60)


def select_studio_session(app: AppProject, sessions: list[dict[str, Any]]) -> dict[str, Any] | None:
    if app.pairing_config_id:
        for session in sessions:
            if session.get("configId") == app.pairing_config_id:
                return session
    if not sessions:
        return None
    return sorted(sessions, key=lambda item: str(item.get("createdUtc", "")), reverse=True)[0]


def verify_studio_session(
    studio: StudioMCPClient,
    app: AppProject,
    result: ValidationResult,
    wait_seconds: int,
    poll_interval_seconds: float,
    min_metric_samples: int,
    min_images: int,
    min_tools: int,
    require_fps: bool,
    require_icon: bool,
    require_validation_route: bool,
    require_device_profile_details: bool,
    probe_binary_download: bool,
    require_binary_download_artifact: bool,
) -> None:
    if not app.bundle_id:
        raise RuntimeError("Cannot verify Studio session before resolving the bundle id.")

    deadline = time.monotonic() + wait_seconds
    last_error: str | None = None
    while time.monotonic() <= deadline:
        try:
            sessions_result = studio.call_tool(
                "ansight_list_sessions",
                {
                    "appId": app.bundle_id,
                    "liveOnly": True,
                    "includeHistorical": False,
                    "limit": 25,
                },
            )
            sessions = sessions_result.get("sessions", [])
            session = select_studio_session(app, sessions if isinstance(sessions, list) else [])
            if session:
                update_studio_result_fields(result, session)

                tool_count = verify_studio_tool_catalog(studio, result.studio_session_id)
                result.studio_tool_count = tool_count
                update_studio_app_icon_fields(studio, app, result)
                update_studio_device_profile_fields(studio, result)

                fps_sample_count = 0
                if require_fps:
                    fps_sample_count = get_fps_sample_count(studio, result.studio_session_id)
                    result.studio_fps_sample_count = fps_sample_count

                if require_validation_route:
                    result.studio_validation_route_seen = get_validation_route_seen(studio, result.studio_session_id)

                if probe_binary_download:
                    try:
                        update_studio_binary_download_fields(studio, result)
                    except Exception as error:
                        result.studio_binary_download_error = str(error)

                failures: list[str] = []
                if result.studio_status != "WebSocket Open":
                    failures.append(f"session status is {result.studio_status!r}")
                if (result.studio_metric_sample_count or 0) < min_metric_samples:
                    failures.append(
                        f"metricSampleCount {(result.studio_metric_sample_count or 0)} < {min_metric_samples}"
                    )
                if (result.studio_image_count or 0) < min_images:
                    failures.append(f"imageCount {(result.studio_image_count or 0)} < {min_images}")
                if (result.studio_tool_count or 0) < min_tools:
                    failures.append(f"tool count {(result.studio_tool_count or 0)} < {min_tools}")
                if require_fps and fps_sample_count <= 0:
                    failures.append("no FPS telemetry samples")
                if require_icon and not result.studio_icon_synced:
                    failures.append("app icon was not synced into the Studio session")
                if require_validation_route and not result.studio_validation_route_seen:
                    failures.append(f"validation route {VALIDATION_ROUTE_NAME!r} was not observed")
                if require_device_profile_details and not result.studio_device_profile_details_synced:
                    failures.append("device profile runtime/network details were not synced into the Studio session")
                if probe_binary_download and not result.studio_binary_download_metadata_verified:
                    failures.append(result.studio_binary_download_error or "binary download metadata was not verified")
                if require_binary_download_artifact and not result.studio_binary_download_reassembled:
                    failures.append(result.studio_binary_download_error or "binary download artifact was not reassembled")

                if not failures:
                    result.studio_verified = True
                    result.studio_error = None
                    return
                last_error = "; ".join(failures)
            else:
                last_error = "No live Studio session matched the launched app."
        except Exception as error:
            last_error = str(error)

        time.sleep(poll_interval_seconds)

    result.studio_error = last_error or "Studio verification timed out."
    raise RuntimeError(result.studio_error)


def update_studio_result_fields(result: ValidationResult, session: dict[str, Any]) -> None:
    result.studio_session_id = session.get("sessionId")
    result.studio_status = session.get("status")
    result.studio_metric_sample_count = int(session.get("metricSampleCount") or 0)
    result.studio_image_count = int(session.get("imageCount") or 0)


def verify_studio_tool_catalog(studio: StudioMCPClient, session_id: str | None) -> int:
    if not session_id:
        return 0
    tools_result = studio.call_tool("ansight_list_app_tools", {"sessionId": session_id})
    catalog = tools_result.get("catalog")
    if isinstance(catalog, dict):
        return int(catalog.get("count") or 0)
    tools = tools_result.get("tools")
    if isinstance(tools, list):
        return len(tools)
    return 0


def get_fps_sample_count(studio: StudioMCPClient, session_id: str | None) -> int:
    if not session_id:
        return 0
    telemetry = studio.call_tool(
        "ansight_get_telemetry",
        {
            "sessionId": session_id,
            "types": ["fps"],
            "limit": 1,
        },
    )
    return int(telemetry.get("matchedSampleCount") or 0)


def get_validation_route_seen(studio: StudioMCPClient, session_id: str | None) -> bool:
    if not session_id:
        return False
    artifacts = studio.call_tool(
        "ansight_get_session_artifacts",
        {
            "sessionId": session_id,
            "types": ["logs"],
            "limit": 500,
        },
    )
    return contains_validation_route(artifacts)


def update_studio_binary_download_fields(studio: StudioMCPClient, result: ValidationResult) -> None:
    if not result.studio_session_id:
        raise RuntimeError("Cannot verify binary download without a Studio session id.")
    if result.studio_binary_download_metadata_verified:
        return

    download_id = f"ansight-validation-{int(time.time() * 1000)}"
    response = studio.call_tool(
        "ansight_call_app_tool",
        {
            "sessionId": result.studio_session_id,
            "toolId": "files.begin_binary_download",
            "arguments": {
                "root": "documents",
                "path": f"ansight-validation/{VALIDATION_BINARY_FILE}",
                "downloadId": download_id,
                "chunkBytes": str(VALIDATION_BINARY_CHUNK_BYTES),
            },
        },
    )

    if response.get("responseType") != "tool.result":
        raise RuntimeError(f"Binary download returned responseType {response.get('responseType')!r}.")
    payload = response.get("payload")
    if not isinstance(payload, dict):
        raise RuntimeError("Binary download response payload was not an object.")
    if payload.get("toolId") != "files.begin_binary_download":
        raise RuntimeError(f"Binary download response used unexpected tool id {payload.get('toolId')!r}.")
    if payload.get("success") is not True:
        raise RuntimeError(f"Binary download tool reported failure: {payload.get('message')!r}.")
    result_payload = payload.get("result")
    if not isinstance(result_payload, dict):
        raise RuntimeError("Binary download tool result was not an object.")

    transfer_id = result_payload.get("transferId")
    size_bytes = result_payload.get("sizeBytes")
    if not isinstance(transfer_id, str) or not transfer_id:
        raise RuntimeError("Binary download result did not include a transfer id.")
    if int(size_bytes or -1) != VALIDATION_BINARY_SIZE_BYTES:
        raise RuntimeError(
            f"Binary download sizeBytes {size_bytes!r} did not match {VALIDATION_BINARY_SIZE_BYTES}."
        )
    if result_payload.get("downloadId") != download_id:
        raise RuntimeError("Binary download result did not preserve the requested download id.")
    if result_payload.get("deliveryMode") != "websocket_binary":
        raise RuntimeError("Binary download result did not use websocket_binary delivery.")
    if result_payload.get("wireProtocol") != "ansight.file-transfer.v1":
        raise RuntimeError("Binary download result did not use ansight.file-transfer.v1.")

    result.studio_binary_download_metadata_verified = True
    result.studio_binary_download_size_bytes = int(size_bytes)
    result.studio_binary_download_transfer_id = transfer_id
    result.studio_binary_download_error = None

    artifact_path = result_payload.get("artifactPath")
    if isinstance(artifact_path, str) and artifact_path:
        result.studio_binary_download_artifact_path = artifact_path
        artifact_file = Path(artifact_path)
        if not artifact_file.is_file():
            raise RuntimeError(f"Binary download artifact path does not exist: {artifact_path}")
        artifact_bytes = artifact_file.read_bytes()
        result.studio_binary_download_received_bytes = len(artifact_bytes)
        if artifact_bytes != validation_binary_payload():
            raise RuntimeError("Binary download artifact bytes did not match the validation payload.")
        result.studio_binary_download_reassembled = True
    else:
        received_bytes = result_payload.get("receivedBytes")
        if isinstance(received_bytes, int):
            result.studio_binary_download_received_bytes = received_bytes
        result.studio_binary_download_reassembled = False


def validation_binary_payload() -> bytes:
    return bytes(index % 251 for index in range(VALIDATION_BINARY_SIZE_BYTES))


def contains_validation_route(value: Any) -> bool:
    if isinstance(value, dict):
        for key in ("label", "name", "screenName", "eventLabel"):
            item = value.get(key)
            if isinstance(item, str) and item == VALIDATION_ROUTE_NAME:
                return True
        return any(contains_validation_route(item) for item in value.values())
    if isinstance(value, list):
        return any(contains_validation_route(item) for item in value)
    return isinstance(value, str) and VALIDATION_ROUTE_NAME in value


def update_studio_app_icon_fields(studio: StudioMCPClient, app: AppProject, result: ValidationResult) -> None:
    if not app.bundle_id:
        return
    app_result = studio.call_tool("ansight_get_app", {"appId": app.bundle_id})
    app_payload = app_result.get("app")
    if not isinstance(app_payload, dict):
        return
    icon_path = app_payload.get("iconImagePath")
    result.studio_icon_image_path = icon_path if isinstance(icon_path, str) and icon_path else None
    result.studio_icon_synced = result.studio_icon_image_path is not None


def update_studio_device_profile_fields(studio: StudioMCPClient, result: ValidationResult) -> None:
    if not result.studio_session_id:
        return

    resource = studio.request(
        "resources/read",
        {"uri": f"ansight://sessions/{result.studio_session_id}/device-profile"},
    )
    for item in resource.get("contents", []):
        if not isinstance(item, dict):
            continue
        text = item.get("text")
        if not isinstance(text, str) or not text.strip():
            continue
        try:
            payload = json.loads(text)
        except json.JSONDecodeError:
            continue
        profile = payload.get("profile")
        if not isinstance(profile, dict):
            continue

        update_studio_session_icon_fields_from_profile(result, profile)
        update_studio_device_profile_detail_fields(result, profile, text)
        return


def update_studio_session_icon_fields_from_profile(result: ValidationResult, profile: dict[str, Any]) -> None:
    app = profile.get("app")
    if not isinstance(app, dict):
        return
    icon = app.get("icon")
    if not isinstance(icon, dict):
        return

    data_base64 = icon.get("dataBase64")
    byte_count = icon.get("byteCount")
    width = icon.get("width")
    height = icon.get("height")
    has_icon = (
        isinstance(data_base64, str)
        and bool(data_base64.strip())
        and isinstance(byte_count, int)
        and byte_count > 0
        and isinstance(width, int)
        and width > 0
        and isinstance(height, int)
        and height > 0
    )
    if has_icon:
        result.studio_session_icon_synced = True
        result.studio_session_icon_width = width
        result.studio_session_icon_height = height
        result.studio_session_icon_byte_count = byte_count
        result.studio_icon_synced = True


def update_studio_device_profile_detail_fields(
    result: ValidationResult,
    profile: dict[str, Any],
    raw_resource_text: str,
) -> None:
    runtime = profile.get("runtime")
    if isinstance(runtime, dict):
        primary = runtime.get("primary")
        if isinstance(primary, int):
            result.studio_device_profile_runtime_code = primary

    device = profile.get("device")
    if isinstance(device, dict):
        network = device.get("network")
        if isinstance(network, dict):
            transport_code = network.get("transportCode")
            if isinstance(transport_code, int):
                result.studio_device_profile_network_transport_code = transport_code

        gpu = device.get("gpu")
        if isinstance(gpu, dict):
            api_code = gpu.get("apiCode")
            if isinstance(api_code, int):
                result.studio_device_profile_gpu_api_code = api_code

    graphics = profile.get("graphics")
    if isinstance(graphics, dict):
        render_backend_code = graphics.get("renderBackendCode")
        if isinstance(render_backend_code, int):
            result.studio_device_profile_render_backend_code = render_backend_code

    app = profile.get("app")
    if isinstance(app, dict):
        environment_code = app.get("environmentCode")
        if isinstance(environment_code, int):
            result.studio_device_profile_environment_code = environment_code

    lower_resource_text = raw_resource_text.lower()
    result.studio_device_profile_privacy_safe = '"ssid"' not in lower_resource_text and '"wifiname"' not in lower_resource_text
    result.studio_device_profile_details_synced = (
        result.studio_device_profile_runtime_code is not None
        and result.studio_device_profile_network_transport_code is not None
        and result.studio_device_profile_environment_code in {1, 3}
        and result.studio_device_profile_privacy_safe
        and runtime_stack_has_runtime_code(profile)
    )


def runtime_stack_has_runtime_code(profile: dict[str, Any]) -> bool:
    runtime = profile.get("runtime")
    if not isinstance(runtime, dict):
        return False
    stack = runtime.get("stack")
    if not isinstance(stack, list):
        return False
    return any(isinstance(item, dict) and isinstance(item.get("runtimeCode"), int) for item in stack)


def filter_projects(projects: list[AppProject], requested_apps: list[str]) -> list[AppProject]:
    if not requested_apps:
        return projects

    selected: list[AppProject] = []
    for request in requested_apps:
        request_path = Path(request).expanduser()
        matched = [
            project
            for project in projects
            if project.slug == request
            or request in project.slug
            or request_path.resolve() == project.root.resolve()
            or request_path.resolve() == project.project_path.resolve()
        ]
        selected.extend(matched)

    deduped: dict[Path, AppProject] = {}
    for project in selected:
        deduped[project.project_path] = project
    return list(deduped.values())


def write_results(output_root: Path, results: list[ValidationResult]) -> Path:
    output_root.mkdir(parents=True, exist_ok=True)
    path = output_root / "ios-test-app-validation-results.json"
    path.write_text(
        json.dumps([dataclasses.asdict(result) for result in results], indent=2),
        encoding="utf-8",
    )
    return path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--test-apps-root", type=Path, default=DEFAULT_TEST_APPS_ROOT)
    parser.add_argument("--sdk-package", type=Path, default=DEFAULT_SDK_PACKAGE)
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    parser.add_argument("--pairing-config-dir", type=Path, default=DEFAULT_OUTPUT_ROOT / "pairing-configs")
    parser.add_argument("--pairing-config", type=Path, default=None, help="Use one explicit pairing config JSON for the selected app.")
    parser.add_argument("--host-address", default="127.0.0.1", help="Host address to add to Studio pairing configs for simulator validation.")
    parser.add_argument("--discovery-port", type=int, default=45123)
    parser.add_argument("--app", action="append", default=[], help="App slug, substring, root path, or xcodeproj path to validate. Repeatable.")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--configuration", default="Debug")
    parser.add_argument("--deployment-target", default="15.0", help="IPHONEOS_DEPLOYMENT_TARGET override passed to xcodebuild.")
    parser.add_argument("--build-settings-timeout-seconds", type=int, default=BUILD_SETTINGS_TIMEOUT_SECONDS)
    parser.add_argument("--build-timeout-seconds", type=int, default=BUILD_TIMEOUT_SECONDS)
    parser.add_argument("--simulator", default=None, help="Simulator UDID. Defaults to the first booted iOS simulator.")
    parser.add_argument("--inventory", action="store_true", help="Only list discovered projects.")
    parser.add_argument("--inventory-details", action="store_true", help="Resolve and print scheme, target, bundle id, and app name as JSON.")
    parser.add_argument("--prepare-only", action="store_true", help="Inject SDK validation files and project references but do not build.")
    parser.add_argument("--build-only", action="store_true", help="Build after preparing but do not install or launch.")
    parser.add_argument("--pod-install", action="store_true", help="Run pod install in selected app roots that contain a Podfile before resolving/building.")
    parser.add_argument("--pod-install-timeout-seconds", type=int, default=900)
    parser.add_argument(
        "--exclude-simulator-arm64",
        action="store_true",
        help="Pass EXCLUDED_ARCHS[sdk=iphonesimulator*]=arm64 for older pods that only ship device arm64 static libraries.",
    )
    parser.add_argument(
        "--inject-validation-app-icon",
        action="store_true",
        help="Inject a deterministic app icon asset catalog and make it the target app icon for validation.",
    )
    parser.add_argument(
        "--inject-validation-route-resolver",
        action="store_true",
        help="Inject an Ansight screen route resolver that renames automatic screen-view captures for validation.",
    )
    parser.add_argument("--studio-daemon", type=Path, default=DEFAULT_STUDIO_DAEMON)
    parser.add_argument("--studio-issue-configs", action="store_true", help="Issue fresh Ansight Studio pairing configs for each app before preparing.")
    parser.add_argument("--studio-config-duration", default="12h", help="Lifetime for pairing configs issued with --studio-issue-configs.")
    parser.add_argument("--studio-verify", action="store_true", help="After launch, verify the live session through Ansight Studio MCP.")
    parser.add_argument("--studio-wait-seconds", type=int, default=25)
    parser.add_argument("--studio-poll-interval", type=float, default=2.0)
    parser.add_argument("--studio-min-metric-samples", type=int, default=1)
    parser.add_argument("--studio-min-images", type=int, default=1)
    parser.add_argument("--studio-min-tools", type=int, default=1)
    parser.add_argument("--studio-no-require-fps", action="store_true", help="Do not require FPS telemetry during Studio verification.")
    parser.add_argument("--studio-require-icon", action="store_true", help="Fail Studio verification unless the app icon is synced into Studio.")
    parser.add_argument(
        "--studio-require-validation-route",
        action="store_true",
        help=f"Fail Studio verification unless the {VALIDATION_ROUTE_NAME!r} screen-view event is observed.",
    )
    parser.add_argument(
        "--studio-require-device-profile-details",
        action="store_true",
        help="Fail Studio verification unless runtime, coarse network, environment, and privacy-safe device profile fields are observed.",
    )
    parser.add_argument(
        "--studio-probe-binary-download",
        action="store_true",
        help="Call files.begin_binary_download for the injected validation binary and record the live Studio response.",
    )
    parser.add_argument(
        "--studio-require-binary-download-artifact",
        action="store_true",
        help="Fail Studio verification unless the validation binary download is reassembled into a host artifact path.",
    )
    return parser.parse_args()


def first_booted_simulator() -> str:
    result = run(["xcrun", "simctl", "list", "devices", "booted", "-j"], check=True, timeout=30)
    parsed = json.loads(result.stdout)
    for runtimes in parsed.get("devices", {}).values():
        for device in runtimes:
            if device.get("state") == "Booted" and device.get("isAvailable", True):
                return device["udid"]
    raise RuntimeError("No booted iOS simulator found. Boot one or pass --simulator.")


def main() -> int:
    global BUILD_SETTINGS_TIMEOUT_SECONDS, BUILD_TIMEOUT_SECONDS
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)
        sys.stderr.reconfigure(line_buffering=True)

    args = parse_args()
    args.test_apps_root = args.test_apps_root.expanduser().resolve()
    args.sdk_package = args.sdk_package.expanduser().resolve()
    args.output_root = args.output_root.expanduser().resolve()
    args.pairing_config_dir = args.pairing_config_dir.expanduser().resolve()
    if args.pairing_config:
        args.pairing_config = args.pairing_config.expanduser().resolve()
    args.studio_daemon = args.studio_daemon.expanduser().resolve()
    BUILD_SETTINGS_TIMEOUT_SECONDS = args.build_settings_timeout_seconds
    BUILD_TIMEOUT_SECONDS = args.build_timeout_seconds
    projects = discover_projects(args.test_apps_root)
    projects = filter_projects(projects, args.app)
    if args.limit is not None:
        projects = projects[: args.limit]

    if args.inventory:
        for project in projects:
            print(f"{project.slug}\t{project.project_path}")
        return 0

    if args.inventory_details:
        details: list[dict[str, Any]] = []
        for project in projects:
            try:
                resolved = resolve_project_identity_fast(project)
                details.append(
                    {
                        "slug": resolved.slug,
                        "project": str(resolved.project_path),
                        "workspace": str(resolved.workspace_path) if resolved.workspace_path else None,
                        "scheme": resolved.scheme,
                        "target": resolved.target_name,
                        "bundleId": resolved.bundle_id,
                        "appName": resolved.app_name,
                        "status": "resolved",
                    }
                )
            except Exception as error:
                details.append(
                    {
                        "slug": project.slug,
                        "project": str(project.project_path),
                        "workspace": str(project.workspace_path) if project.workspace_path else None,
                        "status": "failed",
                        "error": str(error),
                    }
                )
        print(json.dumps(details, indent=2))
        return 0

    if not projects:
        print("No matching iOS app projects found.", file=sys.stderr)
        return 2

    destination_id = args.simulator or first_booted_simulator()
    boot_simulator(destination_id)

    results: list[ValidationResult] = []
    studio_client: StudioMCPClient | None = None
    try:
        if args.studio_issue_configs or args.studio_verify:
            studio_client = StudioMCPClient(args.studio_daemon)
            studio_client.start()

        for project in projects:
            result = ValidationResult(
                slug=project.slug,
                project=str(project.project_path),
                scheme=project.scheme,
                bundle_id=project.bundle_id,
            )
            results.append(result)
            derived_data_path = args.output_root / "DerivedData" / slugify(project.slug)
            stage = "starting"
            try:
                if args.pod_install:
                    stage = "pod_install"
                    print(f"==> Checking CocoaPods dependencies for {project.slug}")
                    result.pod_install_attempted = (project.root / "Podfile").exists()
                    result.pod_install_succeeded = install_cocoapods_dependencies(
                        project,
                        args.pod_install_timeout_seconds,
                    )

                if args.studio_issue_configs:
                    stage = "resolving"
                    if studio_client is None:
                        raise RuntimeError("Studio MCP client was not started.")
                    print(f"==> Resolving {project.slug} for Studio pairing")
                    resolved = resolve_project_identity(
                        project,
                        args.configuration,
                        destination_id,
                        derived_data_path,
                    )
                    result.scheme = resolved.scheme
                    result.bundle_id = resolved.bundle_id
                    result.app_name = resolved.app_name
                    stage = "issuing_pairing_config"
                    print(f"==> Issuing Studio pairing config for {project.slug} ({project.bundle_id})")
                    write_issued_pairing_config(
                        studio_client,
                        project,
                        args.pairing_config_dir,
                        args.studio_config_duration,
                    )
                    result.pairing_config_id = project.pairing_config_id

                stage = "preparing"
                print(f"==> Preparing {project.slug}")
                prepared = prepare_project(
                    project,
                    args.sdk_package,
                    args.pairing_config_dir,
                    args.pairing_config,
                    args.host_address,
                    args.discovery_port,
                    args.configuration,
                    destination_id,
                    derived_data_path,
                    args.inject_validation_app_icon,
                    args.inject_validation_route_resolver,
                )
                result.prepared = True
                result.validation_app_icon_injected = args.inject_validation_app_icon
                result.validation_route_resolver_injected = args.inject_validation_route_resolver
                result.scheme = prepared.scheme
                result.bundle_id = prepared.bundle_id
                result.app_name = prepared.app_name
                result.pairing_config_id = prepared.pairing_config_id or result.pairing_config_id

                if args.prepare_only:
                    result.status = "prepared"
                    continue

                stage = "building"
                print(f"==> Building {project.slug} ({project.bundle_id})")
                build_app(
                    project,
                    args.configuration,
                    destination_id,
                    derived_data_path,
                    args.deployment_target,
                    args.exclude_simulator_arm64,
                )
                result.built = True

                stage = "resolving_app_path"
                app_path = built_app_path(project, args.configuration, destination_id, derived_data_path)
                result.app_path = str(app_path)
                if args.build_only:
                    result.status = "built"
                    continue

                stage = "installing_launching"
                print(f"==> Installing and launching {project.slug}")
                install_and_launch(project, app_path, destination_id)
                result.installed = True
                result.launched = True
                result.launched_at_utc = utc_now_iso()

                if args.studio_verify:
                    stage = "studio_verification"
                    if studio_client is None:
                        raise RuntimeError("Studio MCP client was not started.")
                    print(f"==> Verifying {project.slug} in Ansight Studio")
                    verify_studio_session(
                        studio_client,
                        project,
                        result,
                        args.studio_wait_seconds,
                        args.studio_poll_interval,
                        args.studio_min_metric_samples,
                        args.studio_min_images,
                        args.studio_min_tools,
                        not args.studio_no_require_fps,
                        args.studio_require_icon,
                        args.studio_require_validation_route,
                        args.studio_require_device_profile_details,
                        args.studio_probe_binary_download or args.studio_require_binary_download_artifact,
                        args.studio_require_binary_download_artifact,
                    )
                    result.status = "verified"
                else:
                    result.status = "launched"
                    time.sleep(2)
            except Exception as error:
                result.status = "failed"
                result.failure_stage = stage
                result.error_summary = summarize_error(error)
                result.error = str(error)
                print(f"ERROR: {project.slug}: {error}", file=sys.stderr)
            finally:
                write_results(args.output_root, results)
    finally:
        if studio_client is not None:
            studio_client.close()

    results_path = write_results(args.output_root, results)
    print(f"Wrote {results_path}")
    return 0 if all(result.status != "failed" for result in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
