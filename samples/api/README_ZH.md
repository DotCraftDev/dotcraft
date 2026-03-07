# DotCraft API Samples

**中文 | [English](./README.md)**

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
| [basic_chat.py](./basic_chat.py) | 基本对话（非流式） |
| [streaming_chat.py](./streaming_chat.py) | 流式输出 |
| [multi_turn_chat.py](./multi_turn_chat.py) | 多轮对话（交互式 REPL） |
| [human_in_the_loop.py](./human_in_the_loop.py) | Human-in-the-Loop 审批流程 |

## Human-in-the-Loop 审批

`human_in_the_loop.py` 演示了交互式审批流程。需要将服务器配置为 `ApprovalMode: "interactive"`：

```json
{
    "Api": {
        "Enabled": true,
        "ApprovalMode": "interactive"
    }
}
```

当 Agent 执行敏感操作（如文件写入、Shell 命令）时，操作会暂停并等待 API 客户端通过 `/v1/approvals` 端点进行审批。

流程：
1. 客户端发送聊天请求（Agent 开始执行）
2. Agent 遇到需要审批的操作 → 暂停
3. 客户端轮询 `GET /v1/approvals` 获取待审批列表
4. 用户审批或拒绝 → `POST /v1/approvals/{id}`
5. Agent 恢复执行

详见 [API 模式指南](../../docs/api_guide.md)。
