# DotCraft API Samples

**[中文](./README_ZH.md) | English**

Python examples for DotCraft API mode. DotCraft exposes a standard OpenAI Chat Completions API, so you can call it directly with the `openai` Python SDK.

## Install Dependencies

```bash
pip install -r requirements.txt
```

## Configuration

All samples connect to `http://localhost:8080` by default. Make sure DotCraft is running in API mode:

```json
{
    "Api": {
        "Enabled": true,
        "Port": 8080
    }
}
```

Update `DOTCRAFT_URL` and `API_KEY` at the top of each sample file to match your server configuration.

## Sample List

| File | Description |
|------|------|
| [basic_chat.py](./basic_chat.py) | Basic chat example (non-streaming) |
| [streaming_chat.py](./streaming_chat.py) | Streaming output example |
| [multi_turn_chat.py](./multi_turn_chat.py) | Multi-turn chat with an interactive REPL |

See the [API Mode Guide](../../docs/api_guide.md) for more details.
