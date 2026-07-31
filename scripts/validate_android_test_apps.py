#!/usr/bin/env python3
"""Prepare and run native Android test apps with the local Ansight Android SDK.

The script writes generated integration files into temporary copies of the
curated Android test apps. The original corpus checkouts are not mutated.
"""

from __future__ import annotations

import argparse
import dataclasses
import datetime as dt
import json
import os
import re
import select
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from typing import Any


DEFAULT_TEST_APPS_ROOT = Path("/Users/matthewrobbins/Development/git/ansight-sdk-test-apps/android")
DEFAULT_SDK_ROOT = Path("/Users/matthewrobbins/Development/git/ansight-sdk/src/android")
DEFAULT_OUTPUT_ROOT = Path("/Users/matthewrobbins/Development/git/ansight-sdk/.ansight-validation")
DEFAULT_WORK_ROOT = Path("/tmp/ansight-android-corpus-validation")
DEFAULT_STUDIO_DAEMON = (
    Path(__file__).resolve().parents[2]
    / "ansight/ansight.studio/Ansight.McpStdio/bin/Debug/net10.0/ansight-daemon"
)
DEFAULT_ANDROID_SDK_ARTIFACT = "ai.ansight:ansight-android:1.1.0-preview.1"
# Keep older projects on a platform their bundled AGP/aapt2 can parse. The
# Ansight AARs do not require consumers to compile against the SDK's own
# compileSdk (35), and 33 is sufficient for the injected validation surface.
DEFAULT_COMPILE_SDK = 33
DEFAULT_MIN_SDK = 23
JDK_HOME_BY_MAJOR = {
    8: Path("/Library/Java/JavaVirtualMachines/temurin-8.jdk/Contents/Home"),
    11: Path("/Library/Java/JavaVirtualMachines/legacy - microsoft-11.jdk/Contents/Home"),
    17: Path("/Library/Java/JavaVirtualMachines/microsoft-17.jdk/Contents/Home"),
    21: Path("/Library/Java/JavaVirtualMachines/microsoft-21.jdk.disabled/Contents/Home"),
}
BUILD_TIMEOUT_SECONDS = 900
SDK_PUBLISH_TIMEOUT_SECONDS = 900
VALIDATION_PROVIDER_CLASS = "ai.ansight.validation.AnsightValidationProvider"
VALIDATION_PROVIDER_SOURCE = Path("src/main/java/ai/ansight/validation/AnsightValidationProvider.java")
VALIDATION_RESULTS_FILE = "android-test-app-validation-results.json"
VALIDATION_SUMMARY_FILE = "android-test-app-validation-summary.json"
VALIDATION_INVENTORY_FILE = "android-test-app-inventory.json"
VALIDATION_INIT_SCRIPT_FILE = "android-test-app-validation.init.gradle"
VALIDATION_BINARY_FILE = "large-transfer.bin"
VALIDATION_BINARY_SIZE_BYTES = 150_000

ANDROID_NS = "http://schemas.android.com/apk/res/android"
TOOLS_NS = "http://schemas.android.com/tools"
REQUIRED_NETWORK_PERMISSIONS = (
    "android.permission.INTERNET",
    "android.permission.ACCESS_NETWORK_STATE",
)
EXCLUDED_COPY_NAMES = {
    ".ansight-validation",
    ".cxx",
    ".git",
    ".gradle",
    ".idea",
    ".kotlin",
    "build",
    "captures",
}


@dataclasses.dataclass(frozen=True)
class CommandResult:
    command: list[str]
    cwd: Path | None
    returncode: int
    stdout: str
    stderr: str


@dataclasses.dataclass
class AndroidAppProject:
    slug: str
    source_root: Path
    module_rel: Path
    manifest_rel: Path
    repository: str | None = None
    summary: str | None = None
    metadata_path: Path | None = None
    application_id: str | None = None
    namespace: str | None = None


@dataclasses.dataclass
class ValidationResult:
    slug: str
    source_path: str
    worktree_path: str | None = None
    module_path: str | None = None
    manifest_path: str | None = None
    repository: str | None = None
    application_id: str | None = None
    namespace: str | None = None
    prepared: bool = False
    built: bool = False
    installed: bool = False
    launched: bool = False
    launched_at_utc: str | None = None
    apk_path: str | None = None
    gradle_task: str | None = None
    compile_sdk_raised: bool = False
    min_sdk_raised: bool = False
    local_properties_written: bool = False
    validation_provider_injected: bool = False
    validation_dependency_artifact: str | None = None
    validation_bootstrap_exercises_runtime_events: bool = False
    validation_bootstrap_exercises_preferences: bool = False
    validation_bootstrap_exercises_filesystem: bool = False
    validation_bootstrap_exercises_database: bool = False
    validation_protocol_v2_only: bool = False
    enrollment_invite_id: str | None = None
    studio_verified: bool = False
    studio_session_id: str | None = None
    studio_status: str | None = None
    studio_metric_sample_count: int | None = None
    studio_fps_sample_count: int | None = None
    studio_image_count: int | None = None
    studio_tool_count: int | None = None
    auto_reconnect_verified: bool = False
    auto_reconnect_session_id: str | None = None
    auto_reconnect_status: str | None = None
    auto_reconnect_tool_count: int | None = None
    auto_reconnect_error: str | None = None
    studio_error: str | None = None
    command: list[str] | None = None
    status: str = "pending"
    failure_stage: str | None = None
    error_summary: str | None = None
    error: str | None = None
    stdout_tail: str | None = None
    stderr_tail: str | None = None


class ValidationError(RuntimeError):
    def __init__(self, stage: str, message: str) -> None:
        super().__init__(message)
        self.stage = stage


class StudioMCPClient:
    def __init__(
        self,
        daemon_path: Path,
        mcp_url: str | None = None,
        request_timeout_seconds: int = 15,
    ) -> None:
        self.daemon_path = daemon_path
        self.mcp_url = mcp_url
        self.request_timeout_seconds = request_timeout_seconds
        self.process: subprocess.Popen[str] | None = None
        self.next_request_id = 0

    def start(self) -> None:
        if self.process is not None:
            return

        command = [str(self.daemon_path), "mcp-stdio"]
        if self.mcp_url:
            command.extend(["--mcp-url", self.mcp_url])

        self.process = subprocess.Popen(
            command,
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
                    "name": "ansight-android-test-app-validator",
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

        self.close()
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
        if self.process is None or self.process.poll() is not None:
            self.process = None
            self.start()
        if self.process is None:
            raise RuntimeError("Ansight Studio MCP client could not be started.")
        return self.process


def utc_now() -> str:
    return dt.datetime.now(dt.UTC).isoformat().replace("+00:00", "Z")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate local Ansight Android SDK integration against native Android test apps.",
    )
    parser.add_argument("--test-apps-root", type=Path, default=DEFAULT_TEST_APPS_ROOT)
    parser.add_argument("--sdk-root", type=Path, default=DEFAULT_SDK_ROOT)
    parser.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT_ROOT)
    parser.add_argument("--work-root", type=Path, default=DEFAULT_WORK_ROOT)
    parser.add_argument("--sdk-artifact", default=DEFAULT_ANDROID_SDK_ARTIFACT)
    parser.add_argument("--app", action="append", default=[], help="App slug to validate. Can be repeated.")
    parser.add_argument("--limit", type=int, default=None, help="Limit the number of discovered apps to validate.")
    parser.add_argument("--prepare-only", action="store_true", help="Only prepare temporary app copies.")
    parser.add_argument("--skip-sdk-publish", action="store_true", help="Do not publish the local SDK to Maven local first.")
    parser.add_argument("--build-timeout", type=int, default=BUILD_TIMEOUT_SECONDS)
    parser.add_argument("--sdk-publish-timeout", type=int, default=SDK_PUBLISH_TIMEOUT_SECONDS)
    parser.add_argument("--compile-sdk", type=int, default=DEFAULT_COMPILE_SDK)
    parser.add_argument("--min-sdk", type=int, default=DEFAULT_MIN_SDK)
    parser.add_argument("--keep-workdirs", action="store_true", help="Do not delete existing temporary app workdirs first.")
    parser.add_argument("--install", action="store_true", help="Install each successfully built APK to adb.")
    parser.add_argument("--launch", action="store_true", help="Launch each installed APK with adb monkey.")
    parser.add_argument("--device", default=None, help="adb device serial. Defaults to adb's selected device.")
    parser.add_argument(
        "--grant-app-permission",
        action="append",
        default=[],
        metavar="SLUG=PERMISSION",
        help="Grant an app-declared runtime permission after clearing test data. Repeatable.",
    )
    parser.add_argument("--gradle-arg", action="append", default=[], help="Additional Gradle argument. Can be repeated.")
    parser.add_argument("--studio-daemon", type=Path, default=DEFAULT_STUDIO_DAEMON)
    parser.add_argument(
        "--studio-mcp-url",
        default=None,
        help="Optional Studio MCP endpoint override, for example https://localhost:46125/mcp/.",
    )
    parser.add_argument("--studio-verify", action="store_true", help="After launch, verify the live session through Ansight Studio MCP.")
    parser.add_argument("--studio-wait-seconds", type=int, default=25)
    parser.add_argument("--studio-poll-interval", type=float, default=2.0)
    parser.add_argument("--studio-min-metric-samples", type=int, default=1)
    parser.add_argument("--studio-min-images", type=int, default=1)
    parser.add_argument("--studio-min-tools", type=int, default=1)
    parser.add_argument("--studio-no-require-fps", action="store_true", help="Do not require FPS telemetry during Studio verification.")
    return parser.parse_args(argv)


def run_command(
    command: list[str],
    cwd: Path | None,
    timeout: int,
    env: dict[str, str] | None = None,
) -> CommandResult:
    completed = subprocess.run(
        command,
        cwd=str(cwd) if cwd else None,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=timeout,
        check=False,
    )
    return CommandResult(
        command=command,
        cwd=cwd,
        returncode=completed.returncode,
        stdout=completed.stdout,
        stderr=completed.stderr,
    )


def require_success(result: CommandResult, stage: str) -> None:
    if result.returncode != 0:
        raise ValidationError(stage, command_error_summary(result))


def command_error_summary(result: CommandResult) -> str:
    output = "\n".join(part for part in [result.stderr.strip(), result.stdout.strip()] if part)
    if not output:
        return f"Command failed with exit code {result.returncode}: {' '.join(result.command)}"
    return tail(output, 30)


def tail(value: str, line_count: int = 80) -> str:
    lines = value.splitlines()
    return "\n".join(lines[-line_count:])


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def discover_projects(test_apps_root: Path, app_filters: list[str]) -> list[AndroidAppProject]:
    if not test_apps_root.exists():
        raise FileNotFoundError(f"Android test apps root does not exist: {test_apps_root}")

    filters = {item.strip() for item in app_filters if item.strip()}
    metadata_files = sorted(test_apps_root.glob("*/meta-data.json"))
    projects: list[AndroidAppProject] = []
    for metadata_path in metadata_files:
        slug = metadata_path.parent.name
        if filters and slug not in filters:
            continue

        metadata = load_json(metadata_path)
        source = metadata.get("source", {})
        run_evidence = source.get("runEvidence")
        source_root = metadata_path.parent
        module_rel, manifest_rel = module_from_run_evidence(source_root, run_evidence)
        if module_rel is None or manifest_rel is None:
            module_rel, manifest_rel = discover_application_module(source_root)

        project = AndroidAppProject(
            slug=slug,
            source_root=source_root,
            module_rel=module_rel,
            manifest_rel=manifest_rel,
            repository=source.get("repository"),
            summary=source.get("summary"),
            metadata_path=metadata_path,
        )
        project.application_id = parse_application_id(source_root / module_rel)
        project.namespace = parse_namespace(source_root / module_rel)
        projects.append(project)

    if filters:
        discovered = {project.slug for project in projects}
        missing = sorted(filters - discovered)
        if missing:
            raise ValidationError("discovery", f"No Android test app metadata found for: {', '.join(missing)}")

    return projects


def module_from_run_evidence(source_root: Path, run_evidence: str | None) -> tuple[Path | None, Path | None]:
    if not run_evidence:
        return None, None

    evidence_path = Path(run_evidence)
    parts = evidence_path.parts
    for index in range(0, max(0, len(parts) - 2)):
        if parts[index : index + 3] == ("src", "main", "AndroidManifest.xml"):
            module_parts = parts[:index]
            module_rel = Path(*module_parts) if module_parts else Path(".")
            manifest_rel = module_rel / "src/main/AndroidManifest.xml"
            if (source_root / manifest_rel).exists():
                return module_rel, manifest_rel
    return None, None


def discover_application_module(source_root: Path) -> tuple[Path, Path]:
    candidates: list[tuple[int, Path, Path]] = []
    for manifest_path in source_root.rglob("src/main/AndroidManifest.xml"):
        relative_parts = manifest_path.relative_to(source_root).parts
        if any(part in EXCLUDED_COPY_NAMES for part in relative_parts):
            continue
        module_root = manifest_path.parents[2]
        build_file = find_build_file(module_root)
        if build_file is None:
            continue
        build_text = read_text(build_file)
        if "com.android.application" not in build_text and "com.android.feature" not in build_text:
            continue
        module_rel = module_root.relative_to(source_root)
        score = 0
        if module_rel == Path("app"):
            score -= 20
        if module_rel.name == "app":
            score -= 10
        score += len(module_rel.parts)
        candidates.append((score, module_rel, manifest_path.relative_to(source_root)))

    if not candidates:
        raise ValidationError("discovery", f"No Android application module found under {source_root}")

    _, module_rel, manifest_rel = sorted(candidates, key=lambda item: (item[0], str(item[1])))[0]
    return module_rel, manifest_rel


def find_build_file(module_root: Path) -> Path | None:
    for name in ("build.gradle.kts", "build.gradle"):
        candidate = module_root / name
        if candidate.exists():
            return candidate
    return None


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def parse_application_id(module_root: Path) -> str | None:
    build_file = find_build_file(module_root)
    if build_file is not None:
        text = read_text(build_file)
        patterns = [
            r"\bapplicationId\s*=\s*[\"']([^\"']+)[\"']",
            r"\bapplicationId\s+[\"']([^\"']+)[\"']",
            r"\bapplicationId\s*\(\s*[\"']([^\"']+)[\"']\s*\)",
        ]
        for pattern in patterns:
            match = re.search(pattern, text)
            if match:
                return match.group(1)

    manifest_path = module_root / "src/main/AndroidManifest.xml"
    if manifest_path.exists():
        try:
            root = ET.parse(manifest_path).getroot()
            package_name = root.attrib.get("package")
            if package_name:
                return package_name
        except ET.ParseError:
            return None
    return None


def parse_namespace(module_root: Path) -> str | None:
    build_file = find_build_file(module_root)
    if build_file is None:
        return None
    text = read_text(build_file)
    patterns = [
        r"\bnamespace\s*=\s*[\"']([^\"']+)[\"']",
        r"\bnamespace\s+[\"']([^\"']+)[\"']",
    ]
    for pattern in patterns:
        match = re.search(pattern, text)
        if match:
            return match.group(1)
    return None


def copy_project(source_root: Path, worktree_path: Path, keep_existing: bool) -> None:
    if worktree_path.exists() and not keep_existing:
        shutil.rmtree(worktree_path)
    if worktree_path.exists():
        return

    shutil.copytree(
        source_root,
        worktree_path,
        ignore=copy_ignore,
        symlinks=True,
        ignore_dangling_symlinks=True,
    )


def copy_ignore(directory: str, names: list[str]) -> set[str]:
    del directory
    return {name for name in names if name in EXCLUDED_COPY_NAMES or name.endswith(".iml")}


def normalize_gradle_wrappers(project_root: Path) -> None:
    for wrapper in project_root.rglob("gradlew"):
        relative_parts = wrapper.relative_to(project_root).parts
        if any(part in EXCLUDED_COPY_NAMES for part in relative_parts):
            continue
        data = wrapper.read_bytes()
        if data.startswith(b"\xef\xbb\xbf"):
            data = data[3:]
        text = data.decode("utf-8", errors="replace")
        wrapper_jar = wrapper.parent / "gradle/wrapper/gradle-wrapper.jar"
        if wrapper_jar.exists() and not jar_manifest_has_main_class(wrapper_jar):
            text = text.replace(
                '-jar "$APP_HOME/gradle/wrapper/gradle-wrapper.jar"',
                '-classpath "$APP_HOME/gradle/wrapper/gradle-wrapper.jar" org.gradle.wrapper.GradleWrapperMain',
            )
            data = text.encode("utf-8")
        wrapper.write_bytes(data)
        wrapper.chmod(wrapper.stat().st_mode | 0o755)


def jar_manifest_has_main_class(jar_path: Path) -> bool:
    try:
        with zipfile.ZipFile(jar_path) as archive:
            with archive.open("META-INF/MANIFEST.MF") as manifest:
                return b"Main-Class:" in manifest.read()
    except (KeyError, OSError, zipfile.BadZipFile):
        return False


def disable_dependency_verification(project_root: Path) -> None:
    verification_dir = project_root / "gradle"
    for name in ("verification-metadata.xml", "verification-keyring.keys"):
        path = verification_dir / name
        if path.exists():
            disabled = path.with_name(path.name + ".ansight-validation-disabled")
            if disabled.exists():
                disabled.unlink()
            path.rename(disabled)


def prepare_project(
    project: AndroidAppProject,
    work_root: Path,
    keep_workdirs: bool,
    compile_sdk: int,
    min_sdk: int,
) -> tuple[Path, AndroidAppProject, dict[str, bool]]:
    worktree_path = work_root / project.slug
    copy_project(project.source_root, worktree_path, keep_workdirs)
    normalize_gradle_wrappers(worktree_path)
    disable_dependency_verification(worktree_path)

    module_rel = project.module_rel
    manifest_rel = project.manifest_rel
    if not (worktree_path / manifest_rel).exists():
        module_rel, manifest_rel = discover_application_module(worktree_path)

    prepared_project = dataclasses.replace(
        project,
        source_root=worktree_path,
        module_rel=module_rel,
        manifest_rel=manifest_rel,
    )
    module_root = worktree_path / module_rel
    prepared_project.application_id = parse_application_id(module_root)
    prepared_project.namespace = parse_namespace(module_root)

    ensure_local_properties(worktree_path)
    apply_known_build_placeholders(worktree_path)
    sdk_changes = patch_android_sdk_versions(
        worktree_path,
        module_root,
        compile_sdk=compatible_compile_sdk(worktree_path, module_root, compile_sdk),
        min_sdk=min_sdk,
    )
    inject_validation_provider(
        worktree_path / manifest_rel,
        module_root,
        project.slug,
    )
    return worktree_path, prepared_project, {
        "compile_sdk_raised": sdk_changes["compile_sdk_raised"],
        "min_sdk_raised": sdk_changes["min_sdk_raised"],
        "local_properties_written": True,
        "validation_provider_injected": True,
    }


def ensure_local_properties(project_root: Path) -> None:
    android_sdk = android_sdk_path()
    lines: list[str] = []
    local_properties = project_root / "local.properties"
    existing = read_text(local_properties) if local_properties.exists() else ""
    if "sdk.dir=" not in existing and android_sdk is not None:
        lines.append(f"sdk.dir={android_sdk.as_posix()}")

    placeholders = {
        "tmdb_api_key": "ansight-validation",
        "TMDB_API_KEY": "ansight-validation",
        "NY_TIMES_API_KEY": "ansight-validation",
        "NEWS_API_KEY": "ansight-validation",
        "MAPS_API_KEY": "ansight-validation",
    }
    for key, value in placeholders.items():
        if not re.search(rf"^{re.escape(key)}\s*=", existing, flags=re.MULTILINE):
            lines.append(f"{key}={value}")

    if lines:
        prefix = "" if not existing or existing.endswith("\n") else "\n"
        local_properties.write_text(existing + prefix + "\n".join(lines) + "\n", encoding="utf-8")


def apply_known_build_placeholders(project_root: Path) -> None:
    movie_hunt_config = project_root / "buildSrc/src/main/kotlin/Config.kt"
    if not movie_hunt_config.exists():
        return

    text = read_text(movie_hunt_config)
    if "buildConfigField(\"String\", \"TMDB_API_KEY\", TMDB_API_KEY)" not in text:
        return
    if re.search(r"^\s*const\s+val\s+TMDB_API_KEY\b", text, flags=re.MULTILINE):
        return

    movie_hunt_config.write_text(
        text.replace(
            "object Config {",
            r'const val TMDB_API_KEY = "\"ansight-validation\""' + "\n\nobject Config {",
            1,
        ),
        encoding="utf-8",
    )


def android_sdk_path() -> Path | None:
    for name in ("ANDROID_HOME", "ANDROID_SDK_ROOT"):
        value = os.environ.get(name)
        if value:
            return Path(value).expanduser()
    candidate = Path.home() / "Library/Android/sdk"
    return candidate if candidate.exists() else None


def patch_android_sdk_versions(project_root: Path, module_root: Path, compile_sdk: int, min_sdk: int) -> dict[str, bool]:
    build_file = find_build_file(module_root)
    if build_file is None:
        raise ValidationError("prepare", f"No Gradle build file found for module {module_root}")

    text = read_text(build_file)
    text, compile_raised = raise_numeric_gradle_value(
        text,
        [
            r"(\bcompileSdkVersion\s+)(\d+)",
            r"(\bcompileSdkVersion\s*\(\s*)(\d+)(\s*\))",
            r"(\bcompileSdk\s*=\s*)(\d+)",
            r"(\bcompileSdk\s+)(\d+)",
        ],
        compile_sdk,
    )
    text, min_raised = raise_numeric_gradle_value(
        text,
        [
            r"(\bminSdkVersion\s+)(\d+)",
            r"(\bminSdkVersion\s*\(\s*)(\d+)(\s*\))",
            r"(\bminSdk\s*=\s*)(\d+)",
            r"(\bminSdk\s+)(\d+)",
        ],
        min_sdk,
    )
    build_file.write_text(text, encoding="utf-8")
    catalog_compile_raised, catalog_min_raised = patch_version_catalog_sdk_versions(
        project_root,
        compile_sdk=compile_sdk,
        min_sdk=min_sdk,
    )
    return {
        "compile_sdk_raised": compile_raised or catalog_compile_raised,
        "min_sdk_raised": min_raised or catalog_min_raised,
    }


def compatible_compile_sdk(project_root: Path, module_root: Path, requested_compile_sdk: int) -> int:
    version = gradle_wrapper_version(project_root, module_root)
    if version is None:
        return requested_compile_sdk
    major, minor = version
    if major < 7:
        return min(requested_compile_sdk, 30)
    if major == 7 and minor < 3:
        return min(requested_compile_sdk, 31)
    return requested_compile_sdk


def patch_version_catalog_sdk_versions(project_root: Path, compile_sdk: int, min_sdk: int) -> tuple[bool, bool]:
    compile_raised = False
    min_raised = False
    for catalog in project_root.glob("gradle/*.versions.toml"):
        text = read_text(catalog)
        text, catalog_compile_raised = raise_toml_version_value(
            text,
            [r"(\bcompile[-_.]sdk[-_.]version\s*=\s*\")(\d+)(\")"],
            compile_sdk,
        )
        text, catalog_min_raised = raise_toml_version_value(
            text,
            [r"(\bmin[-_.]sdk[-_.]version\s*=\s*\")(\d+)(\")"],
            min_sdk,
        )
        if catalog_compile_raised or catalog_min_raised:
            catalog.write_text(text, encoding="utf-8")
        compile_raised = compile_raised or catalog_compile_raised
        min_raised = min_raised or catalog_min_raised
    return compile_raised, min_raised


def raise_numeric_gradle_value(text: str, patterns: list[str], minimum: int) -> tuple[str, bool]:
    changed = False

    def replace(match: re.Match[str]) -> str:
        nonlocal changed
        value_group_index = 2
        value = int(match.group(value_group_index))
        if value >= minimum:
            return match.group(0)
        changed = True
        prefix = match.group(1)
        suffix = match.group(3) if match.lastindex and match.lastindex >= 3 else ""
        return f"{prefix}{minimum}{suffix}"

    for pattern in patterns:
        text = re.sub(pattern, replace, text)
    return text, changed


def raise_toml_version_value(text: str, patterns: list[str], minimum: int) -> tuple[str, bool]:
    changed = False

    def replace(match: re.Match[str]) -> str:
        nonlocal changed
        value = int(match.group(2))
        if value >= minimum:
            return match.group(0)
        changed = True
        return f"{match.group(1)}{minimum}{match.group(3)}"

    for pattern in patterns:
        text = re.sub(pattern, replace, text)
    return text, changed


def inject_validation_provider(
    manifest_path: Path,
    module_root: Path,
    slug: str,
) -> None:
    ET.register_namespace("android", ANDROID_NS)
    ET.register_namespace("tools", TOOLS_NS)
    tree = ET.parse(manifest_path)
    manifest = tree.getroot()
    ensure_required_network_permissions(manifest)
    application = manifest.find("application")
    if application is None:
        application = ET.SubElement(manifest, "application")
    android_name = f"{{{ANDROID_NS}}}name"
    for child in list(application):
        if child.tag == "provider" and child.attrib.get(android_name) == VALIDATION_PROVIDER_CLASS:
            application.remove(child)

    provider = ET.Element("provider")
    provider.set(android_name, VALIDATION_PROVIDER_CLASS)
    provider.set(f"{{{ANDROID_NS}}}authorities", "${applicationId}.ansight.validation")
    provider.set(f"{{{ANDROID_NS}}}exported", "false")
    provider.set(f"{{{ANDROID_NS}}}initOrder", "100")
    application.append(provider)
    tree.write(manifest_path, encoding="utf-8", xml_declaration=True)

    source_path = module_root / VALIDATION_PROVIDER_SOURCE
    source_path.parent.mkdir(parents=True, exist_ok=True)
    source_path.write_text(
        validation_provider_source(slug),
        encoding="utf-8",
    )


def ensure_required_network_permissions(manifest: ET.Element) -> None:
    android_name = f"{{{ANDROID_NS}}}name"
    tools_node = f"{{{TOOLS_NS}}}node"
    existing: set[str] = set()
    for permission in manifest.findall("uses-permission"):
        permission_name = permission.attrib.get(android_name)
        if permission_name not in REQUIRED_NETWORK_PERMISSIONS:
            continue
        existing.add(permission_name)
        permission.attrib.pop(tools_node, None)

    application_index = next(
        (index for index, child in enumerate(manifest) if child.tag == "application"),
        len(manifest),
    )
    for permission_name in REQUIRED_NETWORK_PERMISSIONS:
        if permission_name in existing:
            continue
        permission = ET.Element("uses-permission")
        permission.set(android_name, permission_name)
        manifest.insert(application_index, permission)
        application_index += 1


def validation_provider_source(slug: str) -> str:
    client_literal = java_string_literal(f"Ansight Android Validation - {slug}")
    return f"""package ai.ansight.validation;

import ai.ansight.Ansight;
import ai.ansight.runtime.AnsightChannels;
import ai.ansight.runtime.AnsightEventType;
import ai.ansight.runtime.Runtime;
import android.app.Application;
import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.content.SharedPreferences;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.net.Uri;
import android.util.Log;
import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

public final class AnsightValidationProvider extends ContentProvider {{
    private static final String TAG = "AnsightValidation";
    private static final String CLIENT_NAME = {client_literal};

    @Override
    public boolean onCreate() {{
        Context context = getContext();
        if (context == null) {{
            return true;
        }}

        Context appContext = context.getApplicationContext();
        if (!(appContext instanceof Application)) {{
            return true;
        }}

        Application application = (Application) appContext;
        try {{
            Ansight.initializeAndActivateDeveloperMode(
                application,
                CLIENT_NAME + " - " + context.getPackageName()
            );
            Runtime.Event(
                "ansight.validation.bootstrap",
                AnsightEventType.Info,
                AnsightChannels.Unspecified,
                "Android validation ContentProvider initialized."
            );
            Runtime.Metric(
                java.lang.Runtime.getRuntime().totalMemory() - java.lang.Runtime.getRuntime().freeMemory(),
                AnsightChannels.Unspecified
            );
            Map<String, String> details = new HashMap<>();
            details.put("source", "AnsightValidationProvider");
            details.put("packageName", context.getPackageName());
            Runtime.ScreenViewed("Ansight Android Validation Bootstrap", details);
            writeValidationArtifacts(application);
        }} catch (Throwable throwable) {{
            Log.w(TAG, "Unable to initialize Ansight validation bootstrap.", throwable);
        }}
        return true;
    }}

    private static void writeValidationArtifacts(Application application) throws Exception {{
        SharedPreferences preferences = application.getSharedPreferences("ansight-validation", Context.MODE_PRIVATE);
        preferences.edit()
            .putString("status", "started")
            .putString("provider", "AnsightValidationProvider")
            .putLong("startedAtEpochMillis", System.currentTimeMillis())
            .apply();

        File directory = new File(application.getFilesDir(), "ansight-validation");
        if (!directory.exists() && !directory.mkdirs()) {{
            throw new IllegalStateException("Unable to create " + directory);
        }}

        File marker = new File(directory, "validation.txt");
        try (FileOutputStream stream = new FileOutputStream(marker, false)) {{
            stream.write(("Ansight Android validation bootstrap for " + application.getPackageName() + "\\n").getBytes(StandardCharsets.UTF_8));
        }}

        File binary = new File(directory, "{VALIDATION_BINARY_FILE}");
        try (FileOutputStream stream = new FileOutputStream(binary, false)) {{
            byte[] buffer = new byte[4096];
            for (int index = 0; index < buffer.length; index++) {{
                buffer[index] = (byte) (index % 251);
            }}
            int remaining = {VALIDATION_BINARY_SIZE_BYTES};
            while (remaining > 0) {{
                int count = Math.min(buffer.length, remaining);
                stream.write(buffer, 0, count);
                remaining -= count;
            }}
        }}

        SQLiteDatabase database = application.openOrCreateDatabase("ansight-validation.db", Context.MODE_PRIVATE, null);
        try {{
            database.execSQL("CREATE TABLE IF NOT EXISTS validation_events (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, created_at INTEGER NOT NULL)");
            database.execSQL(
                "INSERT INTO validation_events(name, created_at) VALUES (?, ?)",
                new Object[] {{"bootstrap", System.currentTimeMillis()}}
            );
        }} finally {{
            database.close();
        }}
    }}

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {{
        return null;
    }}

    @Override
    public String getType(Uri uri) {{
        return null;
    }}

    @Override
    public Uri insert(Uri uri, ContentValues values) {{
        return null;
    }}

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {{
        return 0;
    }}

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {{
        return 0;
    }}
}}
"""


def java_string_literal(value: str) -> str:
    escaped = (
        value.replace("\\", "\\\\")
        .replace("\"", "\\\"")
        .replace("\r", "\\r")
        .replace("\n", "\\n")
        .replace("\t", "\\t")
    )
    return f"\"{escaped}\""


def create_gradle_init_script(output_root: Path, sdk_artifact: str) -> Path:
    output_root.mkdir(parents=True, exist_ok=True)
    script_path = output_root / VALIDATION_INIT_SCRIPT_FILE
    script_path.write_text(
        f"""def ansightValidationArtifact = "{sdk_artifact}"

def addAnsightValidationRepositories = {{ repositories ->
    try {{ repositories.mavenLocal() }} catch (Throwable ignored) {{}}
    try {{ repositories.google() }} catch (Throwable ignored) {{}}
    try {{ repositories.mavenCentral() }} catch (Throwable ignored) {{}}
    try {{ repositories.gradlePluginPortal() }} catch (Throwable ignored) {{}}
    try {{ repositories.jcenter() }} catch (Throwable ignored) {{}}
}}

settingsEvaluated {{ settings ->
    try {{ addAnsightValidationRepositories(settings.pluginManagement.repositories) }} catch (Throwable ignored) {{}}
    try {{ addAnsightValidationRepositories(settings.dependencyResolutionManagement.repositories) }} catch (Throwable ignored) {{}}
}}

allprojects {{ project ->
    try {{ addAnsightValidationRepositories(project.buildscript.repositories) }} catch (Throwable ignored) {{}}
    try {{ addAnsightValidationRepositories(project.repositories) }} catch (Throwable ignored) {{}}

    project.plugins.withId("com.android.application") {{
        def configurationName = project.configurations.findByName("implementation") != null ? "implementation" : "compile"
        def configuration = project.configurations.findByName(configurationName)
        def alreadyPresent = configuration != null && configuration.dependencies.any {{ dependency ->
            dependency.group == "ai.ansight" && dependency.name == "ansight-android"
        }}
        if (!alreadyPresent) {{
            project.dependencies.add(configurationName, ansightValidationArtifact)
        }}
    }}
}}
""",
        encoding="utf-8",
    )
    return script_path


def publish_sdk(
    sdk_root: Path,
    timeout: int,
    version_override: str | None = None,
) -> CommandResult:
    gradle = gradle_command(sdk_root)
    command = gradle + ["publishReleasePublicationToMavenLocal"]
    if version_override:
        command.append(f"-PansightAndroidVersion={version_override}")
    result = run_command(
        command,
        cwd=sdk_root,
        timeout=timeout,
    )
    require_success(result, "sdk_publish")
    return result


def validation_sdk_artifact(default_artifact: str) -> tuple[str, str | None]:
    if default_artifact != DEFAULT_ANDROID_SDK_ARTIFACT:
        return default_artifact, None

    group, artifact, published_version = default_artifact.split(":", maxsplit=2)
    base_version = published_version.split("-", maxsplit=1)[0]
    version = f"{base_version}-validation-{dt.datetime.now(dt.UTC):%Y%m%d%H%M%S}"
    return f"{group}:{artifact}:{version}", version


def gradle_command(project_root: Path, module_root: Path | None = None) -> list[str]:
    wrapper = find_gradle_wrapper(project_root, module_root)
    if wrapper is not None:
        if not os.access(wrapper, os.X_OK):
            return ["sh", str(wrapper)]
        return [str(wrapper)]
    return ["gradle"]


def find_gradle_wrapper(project_root: Path, module_root: Path | None = None) -> Path | None:
    search_roots: list[Path] = []
    if module_root is not None:
        current = module_root
        while True:
            search_roots.append(current)
            if current == project_root or current.parent == current:
                break
            current = current.parent
    search_roots.append(project_root)

    seen: set[Path] = set()
    for root in search_roots:
        wrapper = root / "gradlew"
        if wrapper in seen:
            continue
        seen.add(wrapper)
        if wrapper.exists():
            return wrapper
    return None


def gradle_wrapper_version(project_root: Path, module_root: Path | None = None) -> tuple[int, int] | None:
    wrapper = find_gradle_wrapper(project_root, module_root)
    if wrapper is None:
        return None
    properties = wrapper.parent / "gradle/wrapper/gradle-wrapper.properties"
    if not properties.exists():
        return None
    match = re.search(r"gradle-(\d+)\.(\d+)(?:\.|\-)", read_text(properties))
    if not match:
        return None
    return int(match.group(1)), int(match.group(2))


def gradle_root(project_root: Path, module_root: Path | None = None) -> Path:
    wrapper = find_gradle_wrapper(project_root, module_root)
    return wrapper.parent if wrapper is not None else project_root


def project_requires_java_21(project_root: Path) -> bool:
    patterns = ("VERSION_21", "jvmTarget = \"21\"", "jvmTarget.set(\"21\")", "languageVersion.set(JavaLanguageVersion.of(21))")
    for path in project_root.rglob("*gradle*"):
        relative_parts = path.relative_to(project_root).parts
        if any(part in EXCLUDED_COPY_NAMES for part in relative_parts) or not path.is_file():
            continue
        text = read_text(path)
        if any(pattern in text for pattern in patterns):
            return True
    return False


def select_java_home(project_root: Path, module_root: Path | None = None) -> Path | None:
    if project_requires_java_21(project_root):
        return existing_jdk_home(21)

    version = gradle_wrapper_version(project_root, module_root)
    if version is None:
        return None

    major, minor = version
    if major < 6:
        return existing_jdk_home(8)
    if major < 7 or (major == 7 and minor < 3):
        return existing_jdk_home(11)
    return None


def existing_jdk_home(major: int) -> Path | None:
    path = JDK_HOME_BY_MAJOR.get(major)
    return path if path is not None and path.exists() else None


def gradle_environment(project_root: Path, module_root: Path | None = None) -> dict[str, str]:
    env = os.environ.copy()
    java_home = select_java_home(project_root, module_root)
    if java_home is not None:
        env["JAVA_HOME"] = str(java_home)
        env["PATH"] = f"{java_home / 'bin'}{os.pathsep}{env.get('PATH', '')}"
    return env


def gradle_module_path(module_rel: Path) -> str:
    if str(module_rel) in ("", "."):
        return ":assembleDebug"
    return ":" + ":".join(module_rel.parts) + ":assembleDebug"


def gradle_task_path(project_root: Path, module_root: Path) -> str:
    root = gradle_root(project_root, module_root)
    try:
        module_rel = module_root.relative_to(root)
    except ValueError:
        module_rel = module_root.relative_to(project_root)
    return gradle_module_path(module_rel)


def build_project(
    worktree_path: Path,
    module_rel: Path,
    init_script: Path,
    timeout: int,
    extra_gradle_args: list[str],
) -> CommandResult:
    module_root = worktree_path / module_rel
    root = gradle_root(worktree_path, module_root)
    command = (
        gradle_command(worktree_path, module_root)
        + [
            "--init-script",
            str(init_script),
            "--no-daemon",
            gradle_task_path(worktree_path, module_root),
        ]
        + extra_gradle_args
    )
    return run_command(command, cwd=root, timeout=timeout, env=gradle_environment(worktree_path, module_root))


def find_debug_apk(module_root: Path, device: str | None) -> Path | None:
    apk_root = module_root / "build/outputs/apk"
    if not apk_root.exists():
        return None
    apks = [
        path
        for path in apk_root.rglob("*.apk")
        if "androidTest" not in path.parts and "test" not in path.name.lower()
    ]
    if not apks:
        return None

    device_abis: list[str] = []
    abi_result = run_command(
        adb_command(device) + ["shell", "getprop", "ro.product.cpu.abilist"],
        cwd=None,
        timeout=30,
    )
    if abi_result.returncode == 0:
        device_abis = [
            abi.strip().lower()
            for abi in abi_result.stdout.split(",")
            if abi.strip()
        ]

    def apk_rank(path: Path) -> tuple[int, float]:
        name = path.name.lower()
        flavor_rank = 1 if "fdroid" in name else (-1 if "playstore" in name else 0)
        if "universal" in name:
            compatibility_rank = len(device_abis) + 2
        else:
            matching_abi_index = next(
                (index for index, abi in enumerate(device_abis) if abi in name),
                None,
            )
            if matching_abi_index is not None:
                compatibility_rank = len(device_abis) + 1 - matching_abi_index
            elif any(abi in name for abi in ("arm64-v8a", "armeabi-v7a", "x86_64", "x86")):
                compatibility_rank = 0
            else:
                compatibility_rank = 1
        return compatibility_rank * 10 + flavor_rank, path.stat().st_mtime

    return max(apks, key=apk_rank)


def verify_apk_protocol_generation(apk_path: Path) -> None:
    legacy_schemas = (
        b"ansight.pairing-bootstrap.v1",
        b"ansight.pairing-config.v1",
        b"ansight.pairing-config-document.v1",
        b"ansight.pairing-ticket.v1",
    )
    current_schema = b"ansight.enrollment-invite.v2"

    with zipfile.ZipFile(apk_path) as archive:
        dex_payload = b"".join(
            archive.read(name)
            for name in archive.namelist()
            if re.fullmatch(r"classes\d*\.dex", name)
        )

    if current_schema not in dex_payload:
        raise ValidationError(
            "sdk_protocol_artifact",
            "Built APK does not contain the current Ansight enrollment-invite v2 protocol.",
        )
    found_legacy = [schema.decode("ascii") for schema in legacy_schemas if schema in dex_payload]
    if found_legacy:
        raise ValidationError(
            "sdk_protocol_artifact",
            "Built APK contains removed Ansight v1 protocol schemas: " + ", ".join(found_legacy),
        )


def resolve_built_application_id(module_root: Path) -> str | None:
    manifest_roots = [
        module_root / "build/intermediates/merged_manifest",
        module_root / "build/intermediates/merged_manifests",
        module_root / "build/intermediates/packaged_manifests",
    ]
    manifests: list[Path] = []
    for manifest_root in manifest_roots:
        if manifest_root.exists():
            manifests.extend(manifest_root.rglob("AndroidManifest.xml"))

    for manifest_path in sorted(manifests, key=lambda path: path.stat().st_mtime, reverse=True):
        try:
            package_name = ET.parse(manifest_path).getroot().attrib.get("package")
            if package_name:
                return package_name
        except ET.ParseError:
            continue
    return None


def verify_studio_session(
    studio: StudioMCPClient,
    result: ValidationResult,
    wait_seconds: int,
    poll_interval_seconds: float,
    min_metric_samples: int,
    min_images: int,
    min_tools: int,
    require_fps: bool,
) -> None:
    if not result.application_id:
        raise RuntimeError("Cannot verify Studio session before resolving the Android application id.")

    deadline = time.monotonic() + wait_seconds
    last_error: str | None = None
    while time.monotonic() <= deadline:
        try:
            sessions_result = studio.call_tool(
                "ansight_list_sessions",
                {
                    "appId": result.application_id,
                    "liveOnly": True,
                    "includeHistorical": False,
                    "limit": 25,
                },
            )
            sessions = sessions_result.get("sessions", [])
            session = select_studio_session(result.application_id, sessions if isinstance(sessions, list) else [])
            if session:
                update_studio_result_fields(result, session)
                result.studio_tool_count = verify_studio_tool_catalog(studio, result.studio_session_id)
                fps_sample_count = 0
                if require_fps:
                    fps_sample_count = get_fps_sample_count(studio, result.studio_session_id)
                    result.studio_fps_sample_count = fps_sample_count

                failures: list[str] = []
                if result.studio_status != "WebSocket Open":
                    failures.append(f"session status is {result.studio_status!r}")
                if (result.studio_metric_sample_count or 0) < min_metric_samples:
                    failures.append(f"metricSampleCount {(result.studio_metric_sample_count or 0)} < {min_metric_samples}")
                if (result.studio_image_count or 0) < min_images:
                    failures.append(f"imageCount {(result.studio_image_count or 0)} < {min_images}")
                if (result.studio_tool_count or 0) < min_tools:
                    failures.append(f"tool count {(result.studio_tool_count or 0)} < {min_tools}")
                if require_fps and fps_sample_count <= 0:
                    failures.append("no FPS telemetry samples")

                if not failures:
                    result.studio_verified = True
                    result.studio_error = None
                    return
                last_error = "; ".join(failures)
            else:
                last_error = "No live Studio session matched the launched Android app."
        except Exception as error:
            last_error = str(error)

        time.sleep(poll_interval_seconds)

    result.studio_error = last_error or "Studio verification timed out."
    raise RuntimeError(result.studio_error)


def verify_studio_reconnect(
    studio: StudioMCPClient,
    result: ValidationResult,
    previous_session_id: str,
    wait_seconds: int,
    poll_interval_seconds: float,
    min_tools: int,
) -> None:
    if not result.application_id:
        raise RuntimeError("Cannot verify Studio reconnect before resolving the Android application id.")

    deadline = time.monotonic() + wait_seconds
    last_error: str | None = None
    while time.monotonic() <= deadline:
        try:
            sessions_result = studio.call_tool(
                "ansight_list_sessions",
                {
                    "appId": result.application_id,
                    "liveOnly": True,
                    "includeHistorical": False,
                    "limit": 25,
                },
            )
            sessions = sessions_result.get("sessions", [])
            candidates = [
                session
                for session in (sessions if isinstance(sessions, list) else [])
                if session.get("sessionId") != previous_session_id
            ]
            session = select_studio_session(result.application_id, candidates)
            if session:
                session_id = session.get("sessionId")
                status = session.get("status")
                tool_count = verify_studio_tool_catalog(
                    studio,
                    session_id if isinstance(session_id, str) else None,
                )
                result.auto_reconnect_session_id = session_id
                result.auto_reconnect_status = status
                result.auto_reconnect_tool_count = tool_count

                failures: list[str] = []
                if status != "WebSocket Open":
                    failures.append(f"session status is {status!r}")
                if tool_count < min_tools:
                    failures.append(f"tool count {tool_count} < {min_tools}")
                if not failures:
                    result.auto_reconnect_verified = True
                    result.auto_reconnect_error = None
                    return
                last_error = "; ".join(failures)
            else:
                last_error = "No new live Studio session appeared after the no-invite relaunch."
        except Exception as error:
            last_error = str(error)

        time.sleep(poll_interval_seconds)

    result.auto_reconnect_error = last_error or "Studio reconnect verification timed out."
    raise RuntimeError(result.auto_reconnect_error)


def select_studio_session(application_id: str, sessions: list[dict[str, Any]]) -> dict[str, Any] | None:
    if not sessions:
        return None
    exact = [session for session in sessions if session.get("appId") == application_id]
    candidates = exact or sessions
    return sorted(candidates, key=lambda item: str(item.get("createdUtc", "")), reverse=True)[0]


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


def adb_command(device: str | None) -> list[str]:
    command = ["adb"]
    if device:
        command += ["-s", device]
    return command


def install_apk(apk_path: Path, device: str | None) -> CommandResult:
    return run_command(adb_command(device) + ["install", "-r", str(apk_path)], cwd=None, timeout=180)


def launch_app(application_id: str, device: str | None) -> CommandResult:
    return run_command(
        adb_command(device) + ["shell", "monkey", "-p", application_id, "-c", "android.intent.category.LAUNCHER", "1"],
        cwd=None,
        timeout=60,
    )


def stop_app(application_id: str, device: str | None) -> CommandResult:
    return run_command(
        adb_command(device) + ["shell", "am", "force-stop", application_id],
        cwd=None,
        timeout=30,
    )


def clear_app_data(application_id: str, device: str | None) -> CommandResult:
    return run_command(
        adb_command(device) + ["shell", "pm", "clear", application_id],
        cwd=None,
        timeout=60,
    )


def grant_app_permission(
    application_id: str,
    permission: str,
    device: str | None,
) -> CommandResult:
    return run_command(
        adb_command(device)
        + ["shell", "pm", "grant", application_id, permission],
        cwd=None,
        timeout=30,
    )


def requested_app_permissions(
    entries: list[str],
    slug: str,
) -> list[str]:
    permissions: list[str] = []
    for entry in entries:
        entry_slug, separator, permission = entry.partition("=")
        if not separator or not entry_slug.strip() or not permission.strip():
            raise ValidationError(
                "permission_setup",
                f"Invalid --grant-app-permission value {entry!r}; expected SLUG=PERMISSION.",
            )
        if entry_slug.strip() == slug:
            permissions.append(permission.strip())
    return permissions


def validate_project(
    project: AndroidAppProject,
    args: argparse.Namespace,
    init_script: Path,
    studio_client: StudioMCPClient | None,
) -> ValidationResult:
    result = ValidationResult(
        slug=project.slug,
        source_path=str(project.source_root),
        repository=project.repository,
        validation_dependency_artifact=args.sdk_artifact,
    )
    last_command: CommandResult | None = None

    try:
        worktree_path, prepared_project, changes = prepare_project(
            project,
            args.work_root,
            args.keep_workdirs,
            args.compile_sdk,
            args.min_sdk,
        )
        module_root = worktree_path / prepared_project.module_rel
        manifest_path = worktree_path / prepared_project.manifest_rel
        result.worktree_path = str(worktree_path)
        result.module_path = str(module_root)
        result.manifest_path = str(manifest_path)
        result.application_id = prepared_project.application_id
        result.namespace = prepared_project.namespace
        result.compile_sdk_raised = changes["compile_sdk_raised"]
        result.min_sdk_raised = changes["min_sdk_raised"]
        result.local_properties_written = changes["local_properties_written"]
        result.validation_provider_injected = changes["validation_provider_injected"]
        result.validation_bootstrap_exercises_runtime_events = True
        result.validation_bootstrap_exercises_preferences = True
        result.validation_bootstrap_exercises_filesystem = True
        result.validation_bootstrap_exercises_database = True
        result.prepared = True

        if args.prepare_only:
            result.status = "prepared"
            return result

        result.gradle_task = gradle_task_path(worktree_path, module_root)
        last_command = build_project(
            worktree_path,
            prepared_project.module_rel,
            init_script,
            args.build_timeout,
            args.gradle_arg,
        )
        result.command = last_command.command
        result.stdout_tail = tail(last_command.stdout)
        result.stderr_tail = tail(last_command.stderr)
        require_success(last_command, "build")
        result.built = True
        built_application_id = resolve_built_application_id(module_root)
        if built_application_id:
            result.application_id = built_application_id

        apk_path = find_debug_apk(module_root, args.device)
        if apk_path is not None:
            result.apk_path = str(apk_path)
            verify_apk_protocol_generation(apk_path)
            result.validation_protocol_v2_only = True
        elif args.install or args.launch:
            raise ValidationError("install", f"No debug APK was produced under {module_root}")

        if args.install:
            if apk_path is None:
                raise ValidationError("install", f"No APK available for {project.slug}")
            last_command = install_apk(apk_path, args.device)
            result.command = last_command.command
            result.stdout_tail = tail(last_command.stdout)
            result.stderr_tail = tail(last_command.stderr)
            require_success(last_command, "install")
            result.installed = True
            if args.studio_verify:
                if not result.application_id:
                    raise ValidationError("clear_app_data", f"Could not resolve applicationId for {project.slug}")
                last_command = clear_app_data(result.application_id, args.device)
                require_success(last_command, "clear_app_data")
                for permission in requested_app_permissions(
                    args.grant_app_permission,
                    project.slug,
                ):
                    last_command = grant_app_permission(
                        result.application_id,
                        permission,
                        args.device,
                    )
                    require_success(last_command, "permission_setup")

        if args.launch:
            if not result.application_id:
                raise ValidationError("launch", f"Could not resolve applicationId for {project.slug}")
            if not result.installed:
                raise ValidationError("launch", "Use --install with --launch.")
            last_command = launch_app(result.application_id, args.device)
            result.command = last_command.command
            result.stdout_tail = tail(last_command.stdout)
            result.stderr_tail = tail(last_command.stderr)
            require_success(last_command, "launch")
            result.launched = True
            result.launched_at_utc = utc_now()

        if args.studio_verify:
            if studio_client is None:
                raise ValidationError("studio_verification", "Studio MCP client was not started.")
            try:
                verify_studio_session(
                    studio_client,
                    result,
                    args.studio_wait_seconds,
                    args.studio_poll_interval,
                    args.studio_min_metric_samples,
                    args.studio_min_images,
                    args.studio_min_tools,
                    not args.studio_no_require_fps,
                )
            except Exception as error:
                raise ValidationError("studio_verification", str(error)) from error
            first_session_id = result.studio_session_id
            if not first_session_id:
                raise ValidationError("auto_reconnect", "Initial Studio verification returned no session id.")
            if not result.application_id:
                raise ValidationError("auto_reconnect", f"Could not resolve applicationId for {project.slug}")
            last_command = stop_app(result.application_id, args.device)
            require_success(last_command, "auto_reconnect")
            time.sleep(1.0)
            last_command = launch_app(result.application_id, args.device)
            require_success(last_command, "auto_reconnect")
            try:
                verify_studio_reconnect(
                    studio_client,
                    result,
                    first_session_id,
                    args.studio_wait_seconds,
                    args.studio_poll_interval,
                    args.studio_min_tools,
                )
            except Exception as error:
                raise ValidationError("auto_reconnect", str(error)) from error
            result.status = "verified"
        else:
            result.status = "success"
        return result
    except subprocess.TimeoutExpired as exc:
        result.status = "failed"
        result.failure_stage = "timeout"
        result.error_summary = f"Command timed out after {exc.timeout} seconds."
        result.error = str(exc)
        result.command = list(exc.cmd) if isinstance(exc.cmd, list) else None
        return result
    except ValidationError as exc:
        result.status = "failed"
        result.failure_stage = exc.stage
        result.error_summary = str(exc)
        result.error = str(exc)
        if last_command is not None:
            result.command = last_command.command
            result.stdout_tail = tail(last_command.stdout)
            result.stderr_tail = tail(last_command.stderr)
        return result
    except Exception as exc:  # noqa: BLE001 - validator should keep processing remaining apps.
        result.status = "failed"
        result.failure_stage = "unexpected"
        result.error_summary = str(exc)
        result.error = repr(exc)
        if last_command is not None:
            result.command = last_command.command
            result.stdout_tail = tail(last_command.stdout)
            result.stderr_tail = tail(last_command.stderr)
        return result


def write_inventory(output_root: Path, projects: list[AndroidAppProject]) -> Path:
    output_root.mkdir(parents=True, exist_ok=True)
    path = output_root / VALIDATION_INVENTORY_FILE
    data = [
        {
            "slug": project.slug,
            "repository": project.repository,
            "summary": project.summary,
            "sourcePath": str(project.source_root),
            "modulePath": str(project.module_rel),
            "manifestPath": str(project.manifest_rel),
            "applicationId": project.application_id,
            "namespace": project.namespace,
            "metadataPath": str(project.metadata_path) if project.metadata_path else None,
        }
        for project in projects
    ]
    path.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return path


def write_results(output_root: Path, results: list[ValidationResult], started_at: float) -> tuple[Path, Path]:
    output_root.mkdir(parents=True, exist_ok=True)
    results_path = output_root / VALIDATION_RESULTS_FILE
    summary_path = output_root / VALIDATION_SUMMARY_FILE

    result_data = [dataclasses.asdict(result) for result in results]
    results_path.write_text(json.dumps(result_data, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    counts: dict[str, int] = {}
    for result in results:
        counts[result.status] = counts.get(result.status, 0) + 1

    summary = {
        "generatedAtUtc": utc_now(),
        "durationSeconds": round(time.monotonic() - started_at, 3),
        "total": len(results),
        "counts": counts,
        "prepared": sum(1 for result in results if result.prepared),
        "built": sum(1 for result in results if result.built),
        "installed": sum(1 for result in results if result.installed),
        "launched": sum(1 for result in results if result.launched),
        "studioVerifiedCount": sum(1 for result in results if result.studio_verified),
        "autoReconnectVerifiedCount": sum(1 for result in results if result.auto_reconnect_verified),
        "studioVerificationFailureCount": sum(1 for result in results if result.failure_stage == "studio_verification"),
        "autoReconnectFailureCount": sum(1 for result in results if result.failure_stage == "auto_reconnect"),
        "studioGates": {
            "fps": build_gate_summary(results, lambda result: (result.studio_fps_sample_count or 0) > 0),
            "screenshots": build_gate_summary(results, lambda result: (result.studio_image_count or 0) > 0),
            "remoteTools": build_gate_summary(results, lambda result: (result.studio_tool_count or 0) > 0),
        },
        "failed": [result.slug for result in results if result.status == "failed"],
        "resultsPath": str(results_path),
    }
    summary_path.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return results_path, summary_path


def build_gate_summary(results: list[ValidationResult], predicate: Any) -> dict[str, int]:
    passed = sum(1 for result in results if predicate(result))
    total = sum(
        1
        for result in results
        if result.studio_session_id is not None
        or result.studio_verified
        or result.failure_stage == "studio_verification"
    )
    return {
        "passed": passed,
        "total": total,
        "failed": max(0, total - passed),
    }


def print_result(result: ValidationResult) -> None:
    status = result.status.upper()
    module = result.gradle_task or result.module_path or "unknown-module"
    print(f"[{status}] {result.slug} {module}")
    if result.error_summary:
        print(f"  {result.failure_stage}: {result.error_summary.splitlines()[-1]}")


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    started_at = time.monotonic()
    args.test_apps_root = args.test_apps_root.expanduser().resolve()
    args.sdk_root = args.sdk_root.expanduser().resolve()
    args.output_root = args.output_root.expanduser().resolve()
    args.work_root = args.work_root.expanduser().resolve()
    args.studio_daemon = args.studio_daemon.expanduser().resolve()
    if args.studio_verify:
        args.install = True
        args.launch = True

    studio_client: StudioMCPClient | None = None
    try:
        projects = discover_projects(args.test_apps_root, args.app)
        if args.limit is not None:
            projects = projects[: args.limit]
        args.work_root.mkdir(parents=True, exist_ok=True)
        args.output_root.mkdir(parents=True, exist_ok=True)

        inventory_path = write_inventory(args.output_root, projects)
        print(f"Discovered {len(projects)} Android test app(s). Inventory: {inventory_path}")

        if not args.skip_sdk_publish and not args.prepare_only:
            args.sdk_artifact, validation_version = validation_sdk_artifact(args.sdk_artifact)
            print(f"Publishing Android SDK from {args.sdk_root} to Maven local...")
            publish_result = publish_sdk(
                args.sdk_root,
                args.sdk_publish_timeout,
                validation_version,
            )
            print(f"Published SDK ({publish_result.returncode}) as {args.sdk_artifact}.")

        if args.studio_verify:
            studio_client = StudioMCPClient(args.studio_daemon, args.studio_mcp_url)
            studio_client.start()

        init_script = create_gradle_init_script(args.output_root, args.sdk_artifact)
        results: list[ValidationResult] = []
        for project in projects:
            result = validate_project(project, args, init_script, studio_client)
            results.append(result)
            write_results(args.output_root, results, started_at)
            print_result(result)

        results_path, summary_path = write_results(args.output_root, results, started_at)
        print(f"Results: {results_path}")
        print(f"Summary: {summary_path}")
        return 1 if any(result.status == "failed" for result in results) else 0
    except Exception as exc:  # noqa: BLE001 - keep CLI failure readable.
        print(f"error: {exc}", file=sys.stderr)
        return 2
    finally:
        if studio_client is not None:
            studio_client.close()


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
