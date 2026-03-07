"""
DotCraft API - Streaming Chat Example

Demonstrates streaming responses from DotCraft.
Streaming lets you see the agent's output as it is generated,
which is especially useful for long-running tool calls.

Usage:
    pip install openai
    python streaming_chat.py
"""

import sys
import io

# Fix Windows console encoding issues
if sys.platform == 'win32':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from openai import OpenAI

DOTCRAFT_URL = "http://localhost:8080/dotcraft/v1"
API_KEY = "your-api-access-key"

client = OpenAI(base_url=DOTCRAFT_URL, api_key=API_KEY)


def main():
    stream = client.chat.completions.create(
        model="dotcraft",
        messages=[
            {"role": "user", "content": "Search the web for the latest AI news and summarize."}
        ],
        stream=True,
    )

    for chunk in stream:
        if not chunk.choices:
            continue
        delta = chunk.choices[0].delta
        if delta and delta.content:
            print(delta.content, end="", flush=True)

    print()


if __name__ == "__main__":
    main()
