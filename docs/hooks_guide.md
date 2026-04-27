# DotCraft Hooks 指南

Hooks 让 DotCraft 在生命周期事件中执行外部命令。它适合做格式化、审计、通知、环境检查和安全守卫。第一次使用建议先写一个观察型 Hook，再逐步加入阻止型 Hook。

## 快速开始

在 `.craft/config.json` 中添加：

```json
{
  "Hooks": {
    "Enabled": true,
    "Events": {
      "AfterToolCall": [
        {
          "name": "log-tool-call",
          "type": "command",
          "command": "node .craft/hooks/log-tool-call.js",
          "matcher": ".*",
          "timeoutSeconds": 10
        }
      ]
    }
  }
}
```

示例脚本读取 stdin 中的 JSON，并记录工具调用：

```js
import fs from 'node:fs'

let input = ''
process.stdin.on('data', chunk => (input += chunk))
process.stdin.on('end', () => {
  fs.appendFileSync('.craft/hooks.log', input + '\n')
})
```

## 配置

Hooks 支持全局配置和工作区配置。工作区配置会覆盖或补充全局配置，适合保存项目自己的格式化、审计和通知规则。

| 字段 | 说明 |
|------|------|
| `Hooks.Enabled` | 是否启用 Hooks |
| `Hooks.Events` | 按事件名分组的 Hook 列表 |
| `name` | Hook 名称，便于日志和排查 |
| `type` | 支持 `"command"` |
| `command` | 要执行的 Shell 命令 |
| `matcher` | 匹配工具名的正则；空字符串匹配所有工具相关事件 |
| `timeoutSeconds` | Hook 超时时间 |

完整事件、stdin payload、退出码和示例见 [Hooks 参考](./hooks/reference.md)。

## 使用示例

| 场景 | 事件 | 行为 |
|------|------|------|
| 文件写入后格式化 | `AfterToolCall` | 匹配 `WriteFile` / `EditFile` 后运行 formatter |
| Shell 命令安全守卫 | `BeforeToolCall` | 检查危险命令，返回非零退出码阻止执行 |
| Agent 完成后通知 | `AfterTurn` | 向企业微信、飞书或内部系统发送摘要 |
| 自动化任务审计 | Automations 相关事件 | 写入外部审计日志 |

## 进阶

### 执行模型

Hook 进程通过 stdin 接收 JSON 上下文。DotCraft 等待命令退出，并按退出码决定是否继续当前操作：

| 退出码 | 含义 |
|--------|------|
| `0` | Hook 成功，继续执行 |
| 非 `0` | Hook 失败；阻止型事件会中断当前操作 |

### Matcher

`matcher` 是正则表达式，只对工具相关事件生效。常见写法：

| matcher | 匹配 |
|---------|------|
| `WriteFile|EditFile` | 文件写入和编辑 |
| `Exec` | Shell 命令 |
| `.*` | 所有工具 |

### 最佳实践

- Hook 脚本保持短小，复杂逻辑放到项目脚本中。
- 观察型 Hook 失败时可以在命令末尾加 `|| true`，避免影响主流程。
- 阻止型 Hook 要输出清楚的错误信息，方便用户知道如何修复。
- 不要把敏感 Token 写进仓库，优先使用环境变量或全局配置。

## 故障排查

### Hook 没有执行

确认 `Hooks.Enabled = true`，事件名正确，`matcher` 能匹配当前工具名，并且命令路径相对当前工作区可用。

### Hook 超时

调大 `timeoutSeconds`，或把慢操作放到后台队列。Hook 适合做短任务，不适合长时间阻塞 Agent。

### 可以在 Hook 中修改工具参数吗？

Hook 不支持修改工具参数或执行结果。Hook 只能观察或阻止当前操作。
