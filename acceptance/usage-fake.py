# A stand-in for usage.nodearr.com in acceptance/Run-S8Docker.ps1. It keeps every report posted to
# /v1/report, with the request's headers, in /reports, and answers 204 as the real one does. The
# script checks what was kept against usage/schema/v1/report.schema.json.
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

REPORTS = "/reports"


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length)
        n = len([f for f in os.listdir(REPORTS) if f.endswith(".json")])

        with open(os.path.join(REPORTS, f"{n:03}.json"), "wb") as f:
            f.write(body)

        with open(os.path.join(REPORTS, f"{n:03}.request"), "w", encoding="utf-8") as f:
            json.dump({"method": self.command, "path": self.path, "headers": dict(self.headers.items())}, f)

        self.send_response(204 if self.path == "/v1/report" else 404)
        self.end_headers()

    def log_message(self, format, *args):
        pass


os.makedirs(REPORTS, exist_ok=True)
ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
