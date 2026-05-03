#!/usr/bin/env python3
"""Sample DotCraft process-backed dynamic tool.

The process speaks JSON-RPC 2.0 over stdio. DotCraft sends
`plugin/initialize` once after startup and `plugin/toolCall` for each tool
invocation.
"""

from __future__ import annotations

import json
import sys
from typing import Any


def write_response(response: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(response, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def initialize(request: dict[str, Any]) -> dict[str, Any]:
    return {
        "jsonrpc": "2.0",
        "id": request.get("id"),
        "result": {
            "success": True,
            "structuredResult": {
                "ready": True,
            },
        },
    }


def tool_call(request: dict[str, Any]) -> dict[str, Any]:
    params = request.get("params") or {}
    arguments = params.get("arguments") or {}
    text = str(arguments.get("text", ""))
    call_id = str(params.get("callId", ""))

    result = {
        "success": True,
        "contentItems": [
            {
                "type": "text",
                "text": f"Echo from external-process-echo: {text}",
            }
        ],
        "structuredResult": {
            "echo": text,
            "length": len(text),
            "callId": call_id,
        },
    }
    return {
        "jsonrpc": "2.0",
        "id": request.get("id"),
        "result": result,
    }


def handle_request(request: dict[str, Any]) -> dict[str, Any]:
    method = request.get("method")
    if method == "plugin/initialize":
        return initialize(request)
    if method == "plugin/toolCall":
        return tool_call(request)
    return {
        "jsonrpc": "2.0",
        "id": request.get("id"),
        "error": {
            "code": -32601,
            "message": f"Unknown method: {method}",
        },
    }


def main() -> None:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
            response = handle_request(request)
        except Exception as exc:
            response = {
                "jsonrpc": "2.0",
                "id": None,
                "error": {
                    "code": -32000,
                    "message": str(exc),
                },
            }
        write_response(response)


if __name__ == "__main__":
    main()
