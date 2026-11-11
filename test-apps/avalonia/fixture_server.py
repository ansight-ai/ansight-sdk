#!/usr/bin/env python3
"""Local synthetic HTTP service used by the mobile benchmark fixtures."""
import argparse
import datetime
import hashlib
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

BINARY = bytes((index * 31 + 17) % 256 for index in range(262144))

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/json':
            self.reply(200, b'{"fixture":"avalonia","value":42}', 'application/json')
        elif self.path == '/error':
            self.reply(500, b'{"error":"controlled-benchmark-failure"}', 'application/json')
        elif self.path == '/binary':
            self.reply(200, BINARY, 'application/octet-stream')
        else:
            self.reply(404, b'{"error":"unknown-fixture"}', 'application/json')

    def do_POST(self):
        length = int(self.headers.get('Content-Length', '0'))
        if self.path != '/echo' or not 0 <= length <= 1048576:
            self.reply(400, b'{"error":"invalid-fixture-request"}', 'application/json')
            return
        self.reply(200, self.rfile.read(length), 'application/json')

    def reply(self, status, body, content_type):
        request_id = self.headers.get('X-Ansight-Benchmark', '')
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('X-Ansight-Benchmark', request_id)
        self.end_headers()
        self.wfile.write(body)
        print(json.dumps({'at': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'method': self.command,
                          'path': self.path, 'requestId': request_id, 'status': status, 'bytes': len(body),
                          'sha256': hashlib.sha256(body).hexdigest()}), flush=True)

    def log_message(self, format, *args):
        pass

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=48733)
    arguments = parser.parse_args()
    server = ThreadingHTTPServer(('127.0.0.1', arguments.port), Handler)
    print(json.dumps({'listening': '127.0.0.1', 'port': arguments.port}), flush=True)
    server.serve_forever()
