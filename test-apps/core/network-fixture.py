#!/usr/bin/env python3
"""Bounded transfer endpoint for the simulator/emulator conditioning harness."""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

class Fixture(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def do_GET(self):
        url = urlsplit(self.path)
        if url.path != "/bytes":
            self.send_error(404)
            return
        try:
            count = int(parse_qs(url.query).get("count", ["262144"])[0])
            if not 1 <= count <= 8 * 1024 * 1024: raise ValueError()
        except ValueError:
            self.send_error(400, "count must be 1–8388608")
            return
        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(count))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        try:
            while count:
                size = min(count, 16384)
                self.wfile.write(b"x" * size)
                count -= size
        except (BrokenPipeError, ConnectionResetError):
            pass
    def log_message(self, format, *args): pass

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1", help="Bind address; use the Mac's LAN address for local-capture conditioning.")
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    server = ThreadingHTTPServer((args.host, args.port), Fixture)
    print(f"Network fixture: http://{args.host}:{server.server_port}/bytes?count=262144", flush=True)
    server.serve_forever()
