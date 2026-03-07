# DotCraft Hooks 指南

Hooks 是 DotCraft 的生命周期事件钩子系统，允许你在 Agent 执行的关键节点自动运行外部 Shell 命令。通过 Hooks，你可以实现安全防护、自动格式化、日志记录、通知推送等自定义扩展，无需修改 DotCraft 源代码。

该设计参考了 Claude Code、Cursor 等主流 Agent 工具的 Hooks 实现。

---

## 快速开始

在工作区的 `.craft` 目录下创建 `hooks.json` 文件：

```json
{
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "WriteFile|EditFile",
                "hooks": [
                    {
                        "type": "command",
                        "command": "echo '文件已修改' >> /tmp/dotcraft-hooks.log"
                    }
                ]
            }
        ]
    }
}
```

这个配置会在每次 `WriteFile` 或 `EditFile` 工具执行成功后，将日志写入文件。

---

## 配置文件

Hooks 支持两级配置，与 DotCraft 主配置体系一致：

| 配置文件 | 路径 | 用途 |
|----------|------|------|
| 全局配置 | `~/.craft/hooks.json` | 所有工作区共享的通用 Hooks |
| 工作区配置 | `<workspace>/.craft/hooks.json` | 当前工作区专用的 Hooks |

### 配置合并规则

- 全局 Hooks 先加载
- 工作区 Hooks **追加**到全局 Hooks 之后（同一事件下）
- 工作区配置不会覆盖或移除全局配置，而是叠加执行

### 启用/禁用

Hooks 功能默认启用。可以在 `config.json` 中禁用：

```json
{
    "Hooks": {
        "Enabled": false
    }
}
```

---

## 配置格式

```json
{
    "hooks": {
        "<事件名>": [
            {
                "matcher": "<正则表达式>",
                "hooks": [
                    {
                        "type": "command",
                        "command": "<Shell 命令>",
                        "timeout": 30
                    }
                ]
            }
        ]
    }
}
```

### 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `hooks` | object | 顶层字段，按事件名分组 |
| `<事件名>` | array | 该事件下的匹配器组列表 |
| `matcher` | string | 正则表达式，匹配工具名称。空字符串表示匹配所有工具。仅对工具相关事件生效 |
| `hooks` | array | 匹配后要执行的命令列表 |
| `type` | string | 目前仅支持 `"command"`（Shell 命令） |
| `command` | string | 要执行的 Shell 命令。Linux/macOS 使用 `/bin/bash -c`，Windows 使用 `powershell.exe` |
| `timeout` | number | 命令超时时间（秒），默认 `30` |

---

## 生命周期事件

DotCraft 提供 6 个生命周期事件：

| 事件 | 触发时机 | 可阻止? | stdin JSON 字段 | 典型用途 |
|------|----------|---------|----------------|----------|
| `SessionStart` | 会话创建或恢复时 | 否 | `sessionId` | 加载上下文、初始化环境 |
| `PreToolUse` | 工具执行前 | **是**（exit 2） | `sessionId`, `toolName`, `toolArgs` | 安全审查、权限控制 |
| `PostToolUse` | 工具执行成功后 | 否 | `sessionId`, `toolName`, `toolArgs`, `toolResult` | 自动格式化、日志记录 |
| `PostToolUseFailure` | 工具执行失败后 | 否 | `sessionId`, `toolName`, `toolArgs`, `error` | 错误监控、告警通知 |
| `PrePrompt` | 用户提示发送给 Agent 前 | **是**（exit 2） | `sessionId`, `prompt` | 输入验证、内容过滤 |
| `Stop` | Agent 完成回复后 | 否 | `sessionId` | 测试验证、结果通知 |

---

## 执行模型

### Shell 进程

每个 Hook 命令都作为独立的 Shell 进程执行：

- **Linux/macOS**：`/bin/bash -c '<command>'`
- **Windows**：`powershell.exe -NoLogo -NoProfile -NonInteractive -Command <command>`
- **工作目录**：当前工作区根目录

### stdin 输入

Hook 进程通过 **stdin** 接收 JSON 格式的上下文数据。JSON 字段根据事件类型不同而变化，未使用的字段不会出现在 JSON 中。

**PreToolUse 示例**：

```json
{
    "sessionId": "acp_abc123",
    "toolName": "WriteFile",
    "toolArgs": {
        "filePath": "src/main.cs",
        "content": "// new code"
    }
}
```

**PrePrompt 示例**：

```json
{
    "sessionId": "acp_abc123",
    "prompt": "请帮我重构这个函数"
}
```

**PostToolUse 示例**：

```json
{
    "sessionId": "acp_abc123",
    "toolName": "WriteFile",
    "toolArgs": {
        "filePath": "src/main.cs",
        "content": "// new code"
    },
    "toolResult": "Successfully wrote to src/main.cs"
}
```

**PostToolUseFailure 示例**：

```json
{
    "sessionId": "acp_abc123",
    "toolName": "WriteFile",
    "toolArgs": {
        "filePath": "/etc/passwd",
        "content": "test"
    },
    "error": "Access denied: path is blacklisted"
}
```

### 退出码语义

| 退出码 | 含义 | 行为 |
|--------|------|------|
| `0` | 成功 | 继续执行 |
| `2` | 阻止 | 阻止当前操作（仅 `PreToolUse` 和 `PrePrompt` 支持）。stderr 内容作为阻止原因 |
| 其他 | 异常 | **Fail-Open**：记录警告日志，但不阻止执行 |

> **Fail-Open 设计**：除了退出码 2 以外的所有非零退出码都不会阻止 Agent 工作流。这确保 Hook 脚本的意外错误不会中断正常使用。

### Matcher 正则匹配

`matcher` 字段使用正则表达式匹配工具名称：

| Matcher | 匹配效果 |
|---------|----------|
| `""` (空字符串) | 匹配所有工具 |
| `"WriteFile"` | 仅匹配 WriteFile |
| `"WriteFile\|EditFile"` | 匹配 WriteFile 或 EditFile |
| `".*File"` | 匹配所有以 File 结尾的工具 |
| `"Exec"` | 匹配 Exec（Shell 命令执行） |

正则匹配不区分大小写。

---

## 使用示例

### 示例 1：文件写入后自动格式化

在每次写入或编辑 C# 文件后自动运行 `dotnet format`：

```json
{
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "WriteFile|EditFile",
                "hooks": [
                    {
                        "type": "command",
                        "command": "dotnet format --include $(cat /dev/stdin | jq -r '.toolArgs.filePath // empty') 2>/dev/null || true",
                        "timeout": 60
                    }
                ]
            }
        ]
    }
}
```

### 示例 2：Shell 命令安全守卫

在执行 Shell 命令前检查是否包含危险操作：

```json
{
    "hooks": {
        "PreToolUse": [
            {
                "matcher": "Exec",
                "hooks": [
                    {
                        "type": "command",
                        "command": ".craft/hooks/guard-shell.sh"
                    }
                ]
            }
        ]
    }
}
```

**`.craft/hooks/guard-shell.sh`**：

```bash
#!/bin/bash
# 读取 stdin 中的 JSON 输入
INPUT=$(cat)
COMMAND=$(echo "$INPUT" | jq -r '.toolArgs.command // empty')

# 检查危险命令
if echo "$COMMAND" | grep -qiE '(rm\s+-rf\s+/|mkfs|dd\s+if=|:(){ :|fork)'; then
    echo "检测到危险命令: $COMMAND" >&2
    exit 2  # 阻止执行
fi

exit 0  # 允许执行
```

### 示例 3：工具调用日志记录

记录所有工具调用到日志文件：

```json
{
    "hooks": {
        "PreToolUse": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "INPUT=$(cat); echo \"[$(date -Iseconds)] CALL: $(echo $INPUT | jq -r '.toolName') args=$(echo $INPUT | jq -c '.toolArgs')\" >> .craft/tool-calls.log"
                    }
                ]
            }
        ],
        "PostToolUseFailure": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "INPUT=$(cat); echo \"[$(date -Iseconds)] FAIL: $(echo $INPUT | jq -r '.toolName') error=$(echo $INPUT | jq -r '.error')\" >> .craft/tool-calls.log"
                    }
                ]
            }
        ]
    }
}
```

### 示例 4：会话开始时加载项目上下文

```json
{
    "hooks": {
        "SessionStart": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "echo \"Project: $(basename $(pwd)), Git branch: $(git branch --show-current 2>/dev/null || echo 'N/A')\""
                    }
                ]
            }
        ]
    }
}
```

### 示例 5：Agent 完成后发送通知

```json
{
    "hooks": {
        "Stop": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": "curl -s -X POST 'https://hooks.slack.com/services/YOUR/WEBHOOK/URL' -H 'Content-Type: application/json' -d '{\"text\": \"DotCraft Agent 已完成任务\"}' > /dev/null 2>&1 || true",
                        "timeout": 10
                    }
                ]
            }
        ]
    }
}
```

### 示例 6：提示词内容过滤

阻止包含敏感关键词的提示词：

```json
{
    "hooks": {
        "PrePrompt": [
            {
                "matcher": "",
                "hooks": [
                    {
                        "type": "command",
                        "command": ".craft/hooks/filter-prompt.sh"
                    }
                ]
            }
        ]
    }
}
```

**`.craft/hooks/filter-prompt.sh`**：

```bash
#!/bin/bash
INPUT=$(cat)
PROMPT=$(echo "$INPUT" | jq -r '.prompt // empty')

# 检查敏感内容
if echo "$PROMPT" | grep -qiE '(password|secret|credential|api.?key)'; then
    echo "提示词包含敏感关键词，已被拦截" >&2
    exit 2
fi

exit 0
```

### 示例 7：全局配置 + 工作区配置

**全局配置** (`~/.craft/hooks.json`)：在所有工作区生效的通用安全守卫

```json
{
    "hooks": {
        "PreToolUse": [
            {
                "matcher": "Exec",
                "hooks": [
                    {
                        "type": "command",
                        "command": "INPUT=$(cat); CMD=$(echo $INPUT | jq -r '.toolArgs.command // empty'); if echo $CMD | grep -qiE 'rm\\s+-rf\\s+/'; then echo 'Blocked: dangerous rm -rf /' >&2; exit 2; fi; exit 0"
                    }
                ]
            }
        ]
    }
}
```

**工作区配置** (`<workspace>/.craft/hooks.json`)：项目特定的格式化 Hook

```json
{
    "hooks": {
        "PostToolUse": [
            {
                "matcher": "WriteFile|EditFile",
                "hooks": [
                    {
                        "type": "command",
                        "command": "dotnet format --verbosity quiet 2>/dev/null || true"
                    }
                ]
            }
        ]
    }
}
```

在该工作区中，`PreToolUse` 会执行全局的安全守卫，`PostToolUse` 会执行项目特定的格式化 — 两者叠加生效。

---

## 编写 Hook 脚本的最佳实践

1. **始终读取 stdin**：即使不需要输入数据，也应读取 stdin（如 `cat > /dev/null`），否则进程可能因管道破裂而异常退出
2. **使用 `jq` 解析 JSON**：推荐安装 `jq` 来解析 stdin 中的 JSON 数据
3. **处理错误**：在命令末尾添加 `|| true` 可以确保非关键 Hook 不会意外返回非零退出码
4. **控制超时**：为耗时操作设置合理的 `timeout` 值，避免阻塞 Agent 执行
5. **区分退出码**：仅在确实需要阻止操作时使用 `exit 2`，其他错误使用 `exit 1`（会被 Fail-Open 处理）
6. **stderr 传递信息**：当使用 `exit 2` 阻止操作时，将原因写入 stderr（`echo "reason" >&2`），该信息会作为阻止原因反馈给 Agent
7. **避免交互式命令**：Hook 在后台运行，无法接收用户交互输入

---

## 常见问题

### Hook 执行顺序是什么？

同一事件下的 Hook 按配置文件中的顺序执行（全局 Hooks 先于工作区 Hooks）。如果某个 `PreToolUse` 或 `PrePrompt` Hook 返回退出码 2，后续 Hook 不再执行。

### Hook 超时后会怎样？

超时后进程会被强制终止（包括子进程树），Hook 返回超时错误但不会阻止操作（Fail-Open）。

### 如何调试 Hook？

1. 在 Hook 命令中将日志写入文件：`echo "debug info" >> /tmp/hook-debug.log`
2. 检查 stderr 输出（DotCraft 会记录 Hook 的 stderr）
3. 手动测试 Hook 脚本：`echo '{"toolName":"WriteFile","toolArgs":{}}' | bash .craft/hooks/your-hook.sh`

### 可以在 Hook 中修改工具参数吗？

目前不支持。Hook 的 stdout 不会用于修改工具参数或执行结果。Hook 只能执行"观察"和"阻止"两种操作。

### DotCraft 内置了哪些工具名称？

常见工具名称（用于 `matcher` 配置）：

| 工具名 | 说明 |
|--------|------|
| `Exec` | 执行 Shell 命令 |
| `ReadFile` | 读取文件（路径为目录时列出目录内容） |
| `WriteFile` | 写入文件 |
| `EditFile` | 编辑文件（部分替换） |
| `GrepFiles` | 搜索文件内容 |
| `FindFiles` | 查找文件 |
| `WebFetch` | 抓取网页内容 |
| `WebSearch` | 搜索网页 |
| `SpawnSubagent` | 派生子智能体执行后台任务 |

> 实际可用工具名称取决于启用的工具提供器和模块。可在 Sessions 页签查看每个会话使用的工具，或查看代码了解所有可用工具。
