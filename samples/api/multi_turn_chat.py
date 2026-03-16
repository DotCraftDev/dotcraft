"""
DotCraft API - Multi-turn Conversation Example

Demonstrates maintaining conversation context across multiple turns.
Each request includes the full message history so the agent remembers
previous context within the same conversation.

Usage:
    pip install openai
    python multi_turn_chat.py
"""

import sys
import io

# Fix Windows console encoding issues
if sys.platform == 'win32':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from openai import OpenAI

DOTCRAFT_URL = "http://localhost:8080/v1"
API_KEY = "your-api-access-key"

client = OpenAI(base_url=DOTCRAFT_URL, api_key=API_KEY)


def chat(messages: list[dict]) -> str:
    response = client.chat.completions.create(
        model="dotcraft",
        messages=messages,
    )
    return response.choices[0].message.content or ""


def main():
    messages: list[dict] = []

    print("DotCraft Multi-turn Chat (type 'quit' to exit)")
    print("-" * 50)

    while True:
        user_input = input("\nYou: ").strip()
        if not user_input:
            continue
        if user_input.lower() in ("quit", "exit"):
            break

        messages.append({"role": "user", "content": user_input})
        reply = chat(messages)
        messages.append({"role": "assistant", "content": reply})

        print(f"\nDotCraft: {reply}")


if __name__ == "__main__":
    main()
