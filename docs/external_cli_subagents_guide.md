# External CLI 子代理指南

External CLI 子代理用于把已有 coding agent CLI 以短进程方式接入 DotCraft。相比 `native`，外部 CLI 通常只提供阶段级进度，不提供逐工具调用细节。

本文讲的是 SubAgent runtime profile。`agentRole`、工具策略和递归深度配置见 [SubAgent 配置指南](./subagents_guide.md)。

在 2026-04-22 的更新后，DotCraft 还可以在同一线程里保存外部 CLI 的 session/chat/thread id，并在后续用户反馈时继续同一个外部会话。这个能力默认关闭，需要在 Desktop 的 SubAgents 设置里手动打开。

## 内置 Profile

| 名称 | runtime | 默认 `bin` | headless 入口 | 是否支持 resume |
|------|---------|------------|---------------|-----------------|
| `native` | `native` | - | DotCraft 原生运行时 | 不适用 |
| `codex-cli` | `cli-oneshot` | `codex` | `codex exec` / `codex exec resume` | 支持 |
| `cursor-cli` | `cli-oneshot` | `cursor-agent` | `cursor-agent -p --output-format json --resume ...` | 支持 |
| `custom-cli-oneshot` | `cli-oneshot` | - | 模板 profile，需同名覆盖并补齐 `bin` 后才可用 | 可选 |

当某个 `cli-oneshot` profile 的 `bin` 在本机不可解析时，DotCraft 会自动将其从系统提示词隐藏。

## Desktop 开关

Desktop 的 `Settings > Sub Agents` 页面提供一个工作区级开关：

- `复用外部 CLI 会话`

默认关闭。开启后，DotCraft 才会尝试为支持 resume 的 external CLI profile 复用之前保存的外部会话。

关闭时：

- 每次委派任务都会新建外部 CLI 会话
- 已保存的 session id 不会被删除，只是暂时不使用

## 何时会续接之前的会话

DotCraft 不会盲目复用任何旧会话。匹配规则是：

- 优先按 `profile + label + workingDirectory` 精确匹配
- 如果这次没有 `label`，只有在同一 `profile + workingDirectory` 下仅存在一个候选时才自动续接
- 其余情况一律新建会话

因此，如果你希望主 Agent 在“第一次委派 -> 用户反馈 -> 再次委派”之间继续同一个外部 CLI 会话，最稳妥的方式是：

- 复用同一个 profile
- 复用同一个 label
- 保持相同工作目录

## 快速配置

子代理配置位于 `config.json` 的 `SubAgentProfiles`。

- 全局：`~/.craft/config.json`
- 工作区：`<workspace>/.craft/config.json`

工作区配置会覆盖同名全局 profile。

```json
{
  "SubAgent": {
    "EnableExternalCliSessionResume": true
  },
  "SubAgentProfiles": {
    "my-cli": {
      "runtime": "cli-oneshot",
      "bin": "my-cli",
      "workingDirectoryMode": "workspace",
      "inputMode": "arg",
      "outputFormat": "text",
      "supportsResume": true,
      "resumeArgTemplate": "--resume {sessionId}",
      "resumeSessionIdJsonPath": "session_id"
    }
  }
}
```

## 字段参考

| 字段 | 说明 |
|------|------|
| `runtime` | 运行时类型。外部短进程 CLI 使用 `cli-oneshot` |
| `bin` | CLI 可执行文件名或绝对路径 |
| `args` | 固定参数列表 |
| `workingDirectoryMode` | `workspace` / `specified` |
| `inputMode` | `stdin` / `arg` / `arg-template` / `env` |
| `inputArgTemplate` | `arg-template` 模式的模板 |
| `inputEnvKey` | `env` 模式写入任务文本的环境变量名 |
| `env` | 固定注入到子进程的环境变量 |
| `envPassthrough` | 从父进程复制的环境变量名列表 |
| `outputFormat` | `text` 或 `json` |
| `outputJsonPath` | `json` 模式下提取最终结果的路径 |
| `readOutputFile` | 是否优先读取输出文件作为最终结果 |
| `outputFileArgTemplate` | 输出文件参数模板，支持 `{path}` |
| `supportsResume` | 是否允许 DotCraft 保存并复用外部 session id |
| `resumeArgTemplate` | resume 参数模板，支持 `{sessionId}` |
| `resumeSessionIdJsonPath` | 从 stdout JSON 提取 session id 的路径 |
| `resumeSessionIdRegex` | 当 stdout 不是单个 JSON 对象时，用正则提取 session id |
| `timeout` | 单次运行超时秒数 |
| `maxOutputBytes` | 最大捕获输出字节数 |
| `trustLevel` | 信任等级：`trusted` / `prompt` / `restricted` |
| `permissionModeMapping` | DotCraft 审批模式到 CLI 参数的映射 |

当 `supportsResume=true` 时，必须配置：

- `resumeArgTemplate`
- `resumeSessionIdJsonPath` 或 `resumeSessionIdRegex` 至少一个

## 审批与权限穿透

### Native 子代理

- Native 子代理内部的 `ReadFile/WriteFile/Exec` 会复用当前会话的 `IApprovalService`
- 审批请求会在 command/path 前自动加上前缀：`[subagent:<label>] ...`

### External CLI 子代理

- DotCraft 不拦截外部 CLI 内部工具调用，而是把当前审批模式翻译为启动参数
- 映射入口是 profile 的 `permissionModeMapping`
- resume 参数会插在审批参数之前，但仍然由 DotCraft 决定是否允许 resume

## 厂商 Headless 参考

### `cursor-cli`

DotCraft 内置基础参数：`-p --output-format json`，并在需要续接时追加 `--resume {sessionId}`。

### `codex-cli`

DotCraft 内置基础参数：`exec`，并通过输出文件参数补上 `--skip-git-repo-check --json --output-last-message {path}`。需要续接时会改为 `exec resume {sessionId}` 的形态；interactive / restricted 模式默认附加 `--sandbox read-only`，auto-approve 模式默认附加 `--dangerously-bypass-approvals-and-sandbox`。session id 从 stdout 中的 `thread_id` 提取。
