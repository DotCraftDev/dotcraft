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
| [human_in_the_loop.py](./human_in_the_loop.py) | Human-in-the-loop approval flow |

## Human-in-the-Loop Approval

`human_in_the_loop.py` demonstrates an interactive approval flow. The server must be configured with `ApprovalMode: "interactive"`:

```json
{
    "Api": {
        "Enabled": true,
        "ApprovalMode": "interactive"
    }
}
```

When the agent attempts a sensitive action such as file writes or shell commands, execution pauses and waits for the API client to approve it through the `/v1/approvals` endpoint.

Flow:
1. The client sends a chat request and the agent starts running
2. The agent hits an action that requires approval and pauses
3. The client polls `GET /v1/approvals` for pending approvals
4. The user approves or rejects with `POST /v1/approvals/{id}`
5. The agent resumes execution

See the [API Mode Guide](../../docs/api_guide.md) for more details.
