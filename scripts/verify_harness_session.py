#!/usr/bin/env python3
"""Verify a live first-party harness session through Ansight Studio MCP."""

from __future__ import annotations

import argparse
import json
import select
import subprocess
import time
from pathlib import Path
from typing import Any


DEFAULT_DAEMON = Path(
    "/Users/matthewrobbins/Development/git/ansight/"
    "ansight.studio/Ansight.McpStdio/bin/Debug/net10.0/ansight-daemon"
)
DEFAULT_MCP_URL = "https://localhost:46125/mcp/"


class StudioMcpClient:
    def __init__(self, daemon: Path, mcp_url: str, timeout_seconds: float = 20) -> None:
        self.daemon = daemon
        self.mcp_url = mcp_url
        self.timeout_seconds = timeout_seconds
        self.process: subprocess.Popen[str] | None = None
        self.next_request_id = 0

    def __enter__(self) -> "StudioMcpClient":
        self.start()
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def start(self) -> None:
        if self.process is not None:
            return
        self.process = subprocess.Popen(
            [str(self.daemon), "mcp-stdio", "--mcp-url", self.mcp_url],
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
                "clientInfo": {"name": "ansight-harness-verifier", "version": "1"},
            },
        )
        self.notify("notifications/initialized", {})

    def close(self) -> None:
        if self.process is None:
            return
        self.process.terminate()
        try:
            self.process.wait(timeout=3)
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
        deadline = time.monotonic() + self.timeout_seconds
        while time.monotonic() < deadline:
            remaining = max(0.0, deadline - time.monotonic())
            readable, _, _ = select.select([process.stdout], [], [], remaining)
            if not readable:
                break
            line = process.stdout.readline()
            if not line:
                break
            response = json.loads(line)
            if response.get("id") != request_id:
                continue
            if "error" in response:
                raise RuntimeError(str(response["error"]))
            result = response.get("result")
            return result if isinstance(result, dict) else {}
        self.close()
        raise RuntimeError(f"Timed out waiting for Studio MCP response to {method}.")

    def call_tool(self, name: str, arguments: dict[str, Any]) -> dict[str, Any]:
        result = self.request("tools/call", {"name": name, "arguments": arguments})
        if result.get("isError"):
            message = "\n".join(
                item.get("text", "")
                for item in result.get("content", [])
                if isinstance(item, dict)
            ).strip()
            raise RuntimeError(message or f"Studio tool {name} failed.")
        structured = result.get("structuredContent")
        return structured if isinstance(structured, dict) else result

    def require_process(self) -> subprocess.Popen[str]:
        if self.process is None or self.process.poll() is not None:
            self.process = None
            self.start()
        if self.process is None:
            raise RuntimeError("Could not start the Studio MCP bridge.")
        return self.process


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--app-id", required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--daemon", type=Path, default=DEFAULT_DAEMON)
    parser.add_argument("--mcp-url", default=DEFAULT_MCP_URL)
    parser.add_argument("--wait-seconds", type=int, default=120)
    parser.add_argument("--min-metrics", type=int, default=1)
    parser.add_argument("--min-images", type=int, default=1)
    parser.add_argument("--min-tools", type=int, default=1)
    parser.add_argument("--no-fps", action="store_true")
    return parser.parse_args()


def tool_count(payload: dict[str, Any]) -> int:
    catalog = payload.get("catalog")
    if isinstance(catalog, dict):
        return int(catalog.get("count") or 0)
    tools = payload.get("tools")
    return len(tools) if isinstance(tools, list) else 0


def verify(args: argparse.Namespace) -> dict[str, Any]:
    deadline = time.monotonic() + args.wait_seconds
    last_error = "No live session found."
    with StudioMcpClient(args.daemon, args.mcp_url) as studio:
        while time.monotonic() <= deadline:
            try:
                listing = studio.call_tool(
                    "ansight_list_sessions",
                    {
                        "appId": args.app_id,
                        "liveOnly": True,
                        "includeHistorical": False,
                        "limit": 25,
                    },
                )
                sessions = listing.get("sessions")
                candidates = [
                    item
                    for item in (sessions if isinstance(sessions, list) else [])
                    if item.get("appId") == args.app_id
                ]
                candidates.sort(key=lambda item: str(item.get("createdUtc", "")), reverse=True)
                if not candidates:
                    last_error = "No live Studio session matched the app id."
                    time.sleep(2)
                    continue

                session = candidates[0]
                session_id = str(session.get("sessionId") or "")
                tools = tool_count(studio.call_tool("ansight_list_app_tools", {"sessionId": session_id}))
                fps = 0
                if not args.no_fps:
                    telemetry = studio.call_tool(
                        "ansight_get_telemetry",
                        {"sessionId": session_id, "types": ["fps"], "limit": 1},
                    )
                    fps = int(telemetry.get("matchedSampleCount") or 0)

                result = {
                    "label": args.label,
                    "appId": args.app_id,
                    "sessionId": session_id,
                    "status": session.get("status"),
                    "createdUtc": session.get("createdUtc"),
                    "deviceName": session.get("deviceName"),
                    "platform": session.get("platform"),
                    "metricSampleCount": int(session.get("metricSampleCount") or 0),
                    "imageCount": int(session.get("imageCount") or 0),
                    "toolCount": tools,
                    "fpsSampleCount": fps,
                    "verified": False,
                }
                failures: list[str] = []
                if result["status"] != "WebSocket Open":
                    failures.append(f"status={result['status']!r}")
                if result["metricSampleCount"] < args.min_metrics:
                    failures.append(f"metrics={result['metricSampleCount']} < {args.min_metrics}")
                if result["imageCount"] < args.min_images:
                    failures.append(f"images={result['imageCount']} < {args.min_images}")
                if tools < args.min_tools:
                    failures.append(f"tools={tools} < {args.min_tools}")
                if not args.no_fps and fps < 1:
                    failures.append("fps=0")
                if not failures:
                    result["verified"] = True
                    return result
                last_error = "; ".join(failures)
            except Exception as error:
                last_error = str(error)
            time.sleep(2)
    raise RuntimeError(last_error)


def main() -> int:
    args = parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    try:
        result = verify(args)
    except Exception as error:
        result = {
            "label": args.label,
            "appId": args.app_id,
            "verified": False,
            "error": str(error),
        }
        args.output.write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result, indent=2))
        return 1
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
