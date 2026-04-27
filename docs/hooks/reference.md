# DotCraft Hooks 参考

本页汇总 Hook 事件、配置字段、stdin 输入、退出码语义和常用脚本示例。入门流程见 [Hooks 指南](../hooks_guide.md)。

## 配置格式

```json
{
  "Hooks": {
    "Enabled": true,
    "Events": {
      "BeforeToolCall": [
        {
          "name": "guard-shell",
          "type": "command",
          "command": "node .craft/hooks/guard-shell.js",
          "matcher": "Exec",
          "timeoutSeconds": 10
        }
      ]
    }
  }
}
```

| 字段 | 说明 |
|------|------|
| `name` | Hook 名称 |
| `type` | 命令类型，支持 `"command"` |
| `command` | Shell 命令 |
| `matcher` | 工具名正则；空字符串匹配所有工具相关事件 |
| `timeoutSeconds` | 超时时间 |

## 生命周期事件

| 事件 | 用途 |
|------|------|
| `BeforeToolCall` | 工具调用前检查或阻止 |
| `AfterToolCall` | 工具调用后记录、格式化或通知 |
| `BeforeTurn` | Agent 回合开始前准备上下文 |
| `AfterTurn` | Agent 回合结束后记录结果或通知 |
| `BeforeAutomationTask` | 自动化任务执行前检查 |
| `AfterAutomationTask` | 自动化任务完成后同步状态 |

## stdin 输入

Hook 进程通过 stdin 接收 JSON。字段根据事件类型变化，未使用字段不会出现。

工具相关事件通常包含：

```json
{
  "event": "BeforeToolCall",
  "workspace": "F:\\project",
  "sessionId": "thread-id",
  "toolName": "Exec",
  "arguments": {
    "command": "dotnet test"
  }
}
```

回合相关事件通常包含：

```json
{
  "event": "AfterTurn",
  "workspace": "F:\\project",
  "sessionId": "thread-id",
  "summary": "Agent completed the turn"
}
```

## 退出码语义

| 退出码 | 语义 |
|--------|------|
| `0` | 成功，继续执行 |
| 非 `0` | Hook 失败；`Before*` 事件可阻止当前操作 |

Hook 的 stdout 用于日志和错误说明，不会修改工具参数或执行结果。

## 示例

### 文件写入后自动格式化

```json
{
  "name": "format-after-edit",
  "type": "command",
  "command": "npm run format -- --write",
  "matcher": "WriteFile|EditFile",
  "timeoutSeconds": 60
}
```

### Shell 命令安全守卫

```js
let input = ''
process.stdin.on('data', chunk => (input += chunk))
process.stdin.on('end', () => {
  const payload = JSON.parse(input)
  const command = payload.arguments?.command ?? ''
  if (/rm\s+-rf|Remove-Item\s+-Recurse/i.test(command)) {
    console.error('Dangerous command blocked by workspace Hook.')
    process.exit(1)
  }
})
```

### 工具调用日志

```js
import fs from 'node:fs'

let input = ''
process.stdin.on('data', chunk => (input += chunk))
process.stdin.on('end', () => {
  const payload = JSON.parse(input)
  fs.appendFileSync('.craft/tool-calls.log', JSON.stringify({
    event: payload.event,
    toolName: payload.toolName,
    at: new Date().toISOString()
  }) + '\n')
})
```

## 调试

- 先用观察型 Hook 验证 stdin payload。
- 在脚本中把原始输入写入 `.craft/hooks-debug.log`。
- 命令路径使用工作区相对路径，避免不同入口的 cwd 差异。
- 对非关键 Hook 使用 `|| true`，避免外部服务短暂失败阻塞 Agent。
