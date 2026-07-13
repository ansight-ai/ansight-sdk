#!/usr/bin/env python3
"""Fail CI when tracked Ansight pairing credentials are present.

Pairing documents are bearer credentials until v2 enrollment consumes them.
Examples must use the explicit `.example.ans.json` suffix and placeholder values.
"""

from __future__ import annotations

import json
import pathlib
import subprocess
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
FORBIDDEN_NAMES = {"ansight.json", "ansight.developer-pairing.json"}


def tracked_files() -> list[pathlib.Path]:
    result = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=ROOT,
        check=True,
        capture_output=True,
    )
    return [ROOT / entry.decode() for entry in result.stdout.split(b"\0") if entry]


def is_forbidden_pairing_name(path: pathlib.Path) -> bool:
    relative = path.relative_to(ROOT)
    if path.name.endswith(".example.ans.json"):
        return False
    return path.name in FORBIDDEN_NAMES or path.name.endswith(".ans.json")


def contains_json_credential(path: pathlib.Path) -> bool:
    if path.suffix.lower() != ".json":
        return False
    if any(part.lower() in {"test", "tests", "fixtures"} for part in path.parts):
        return False

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError):
        return False

    pending = [payload]
    while pending:
        value = pending.pop()
        if isinstance(value, dict):
            for key, child in value.items():
                normalized_key = str(key).lower()
                if normalized_key in {
                    "onetimetoken",
                    "websockettoken",
                    "enrollmentsecret",
                } and isinstance(child, str) and len(child.strip()) >= 20:
                    return True
                if normalized_key == "secret" and isinstance(child, str) and len(child.strip()) >= 32:
                    if value.get("ticketId") or value.get("maxUses") == 1:
                        return True
                pending.append(child)
        elif isinstance(value, list):
            pending.extend(value)

    return False


def main() -> int:
    findings: list[str] = []
    for path in tracked_files():
        if not path.exists():
            continue
        if is_forbidden_pairing_name(path):
            findings.append(f"tracked pairing credential filename: {path.relative_to(ROOT)}")
        elif contains_json_credential(path):
            findings.append(f"tracked JSON contains pairing credential material: {path.relative_to(ROOT)}")

    if findings:
        print("Pairing credential check failed:", file=sys.stderr)
        for finding in findings:
            print(f"- {finding}", file=sys.stderr)
        return 1

    print("No tracked pairing credentials detected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
