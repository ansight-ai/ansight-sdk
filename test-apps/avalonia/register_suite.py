#!/usr/bin/env python3
"""Register every benchmark child against the shared source-controlled workspace."""

import argparse
import json
import subprocess
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cli", default="ansight")
    arguments = parser.parse_args()
    root = Path(__file__).resolve().parent
    suite = json.loads((root / "suite.json").read_text())
    app_ids = sorted({app_id for child in suite["children"] for app_id in child["appIds"].values()})
    for app_id in app_ids:
        result = subprocess.run([arguments.cli, "app", "register", app_id, "--codebase", str(root), "--json"],
                                check=True, capture_output=True, text=True)
        json.loads(result.stdout)
        print(json.dumps({"appId": app_id, "workspaceRoot": str(root), "registered": True}), flush=True)


if __name__ == "__main__":
    main()
