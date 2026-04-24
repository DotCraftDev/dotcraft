# DotCraft API Samples

DotCraft API 模式的 Python 使用示例。DotCraft 暴露标准 OpenAI Chat Completions API，可直接使用 `openai` Python SDK 调用。

## 安装依赖

```bash
pip install -r requirements.txt
```

## 配置

所有示例默认连接 `http://localhost:8080`，请确保 DotCraft 已启动 API 模式：

```json
{
    "Api": {
        "Enabled": true,
        "Port": 8080
    }
}
```

修改示例文件顶部的 `DOTCRAFT_URL` 和 `API_KEY` 以匹配你的服务器配置。

## 示例列表

| 文件 | 说明 |
|------|------|
| [basic_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/basic_chat.py) | 基本对话（非流式） |
| [streaming_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/streaming_chat.py) | 流式输出 |
| [multi_turn_chat.py](https://github.com/DotHarness/dotcraft/blob/master/samples/api/multi_turn_chat.py) | 多轮对话（交互式 REPL） |

详见 [API 模式指南](../api_guide)。
