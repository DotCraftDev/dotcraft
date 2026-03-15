"""
DotCraft API - Basic Chat Example

Demonstrates basic non-streaming chat with DotCraft using the OpenAI SDK.
DotCraft exposes an OpenAI-compatible Chat Completions endpoint,
so any standard OpenAI client library works out of the box.

Usage:
    pip install openai
    python basic_chat.py
"""

from openai import OpenAI

DOTCRAFT_URL = "http://localhost:8080/v1"
API_KEY = "your-api-access-key"

client = OpenAI(base_url=DOTCRAFT_URL, api_key=API_KEY)


def main():
    response = client.chat.completions.create(
        model="dotcraft",
        messages=[
            {"role": "user", "content": "List the files in the current workspace directory."}
        ],
    )
    print(response.choices[0].message.content)


if __name__ == "__main__":
    main()
