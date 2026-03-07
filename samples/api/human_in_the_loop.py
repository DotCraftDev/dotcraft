"""
DotCraft API - Human-in-the-Loop Approval Example

When the DotCraft API server is configured with ApprovalMode "interactive",
sensitive operations (file writes, shell commands outside workspace) will pause
and wait for explicit human approval via the /v1/approvals endpoint.

This example shows how to:
1. Send a chat request in a background thread
2. Poll for pending approval requests
3. Prompt the user to approve or reject each request
4. Let the agent resume after decisions are made

Server configuration (config.json):
{
    "Api": {
        "Enabled": true,
        "ApprovalMode": "interactive"
    }
}

Usage:
    pip install openai requests
    python human_in_the_loop.py
"""

import threading
import time

import requests
from openai import OpenAI

DOTCRAFT_BASE = "http://localhost:8080"
DOTCRAFT_URL = f"{DOTCRAFT_BASE}/dotcraft/v1"
API_KEY = "your-api-access-key"

client = OpenAI(base_url=DOTCRAFT_URL, api_key=API_KEY)
headers = {"Authorization": f"Bearer {API_KEY}"}

result_holder: dict = {}


def send_chat_request():
    """Send a chat request that will trigger tool calls needing approval."""
    response = client.chat.completions.create(
        model="dotcraft",
        messages=[
            {
                "role": "user",
                "content": "List the files in your parent directory.",
            }
        ],
    )
    result_holder["response"] = response.choices[0].message.content


def poll_and_approve():
    """Poll for pending approvals and prompt the user to approve/reject."""
    seen: set[str] = set()

    while "response" not in result_holder:
        try:
            resp = requests.get(
                f"{DOTCRAFT_BASE}/v1/approvals",
                headers=headers,
                timeout=5,
            )
            if resp.status_code != 200:
                time.sleep(1)
                continue

            pending = resp.json().get("approvals", [])
            for approval in pending:
                approval_id = approval["id"]
                if approval_id in seen:
                    continue
                seen.add(approval_id)

                print(f"\n{'='*60}")
                print(f"APPROVAL REQUEST: {approval['type']}")
                print(f"  Operation : {approval.get('operation', 'N/A')}")
                print(f"  Detail    : {approval.get('detail', 'N/A')}")
                print(f"{'='*60}")

                decision = input("Approve? [y/N]: ").strip().lower()
                approved = decision in ("y", "yes")

                requests.post(
                    f"{DOTCRAFT_BASE}/v1/approvals/{approval_id}",
                    headers=headers,
                    json={"approved": approved},
                    timeout=5,
                )

                status = "APPROVED" if approved else "REJECTED"
                print(f"  -> {status}")

        except requests.RequestException:
            pass

        time.sleep(0.5)


def main():
    print("Sending request to DotCraft (interactive approval mode)...")
    print("You will be prompted to approve sensitive operations.\n")

    chat_thread = threading.Thread(target=send_chat_request, daemon=True)
    chat_thread.start()

    poll_and_approve()

    chat_thread.join(timeout=30)

    if "response" in result_holder:
        print(f"\nDotCraft: {result_holder['response']}")
    else:
        print("\nRequest timed out.")


if __name__ == "__main__":
    main()
