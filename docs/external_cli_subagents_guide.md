# External CLI 子代理指南

External CLI 子代理用于把已有 coding agent CLI 以 one-shot 方式接入 DotCraft。相比 `dotcraft-native`，外部 CLI 通常只提供阶段级进度，不提供逐工具调用细节。

## 内置 Profile

| 名称 | runtime | 默认 `bin` | headless 入口 |
|------|---------|------------|---------------|
| `dotcraft-native` | `native` | - | DotCraft 原生运行时 |
| `codex-cli` | `cli-oneshot` | `codex` | `codex exec` |
| `cursor-cli` | `cli-oneshot` | `cursor-agent` | `cursor-agent --print --output-format json --mode ask` |
| `custom-cli-oneshot` | `cli-oneshot` | - | 模板 profile，需同名覆盖并补齐 `bin` 后才可用 |

当某个 `cli-oneshot` profile 的 `bin` 在本机不可解析时，DotCraft 会自动将其从系统提示词隐藏；可通过 `InspectSubagentProfiles` 查看隐藏原因。

## 快速配置

子代理配置位于 `config.json` 的 `SubAgentProfiles`。

- 全局：`~/.craft/config.json`
- 工作区：`<workspace>/.craft/config.json`

工作区配置会覆盖同名全局 profile。

```json
{
  "SubAgentProfiles": {
    "my-cli": {
      "runtime": "cli-oneshot",
      "bin": "my-cli",
      "workingDirectoryMode": "workspace",
      "inputMode": "arg",
      "outputFormat": "text"
    }
  }
}
```

## 字段参考

| 字段 | 说明 |
|------|------|
| `runtime` | 运行时类型。外部 one-shot CLI 使用 `cli-oneshot` |
| `bin` | CLI 可执行文件名或绝对路径 |
| `args` | 固定参数列表 |
| `workingDirectoryMode` | `workspace` / `specified` / `worktree` |
| `inputMode` | `stdin` / `arg` / `arg-template` / `env` |
| `inputArgTemplate` | `arg-template` 模式的模板 |
| `inputEnvKey` | `env` 模式写入任务文本的环境变量名 |
| `env` | 固定注入到子进程的环境变量 |
| `envPassthrough` | 从父进程复制的环境变量名列表 |
| `outputFormat` | `text` 或 `json` |
| `outputJsonPath` | `json` 模式下提取最终结果的路径 |
| `readOutputFile` | 是否优先读取输出文件作为最终结果 |
| `outputFileArgTemplate` | 输出文件参数模板，支持 `{path}` |
| `timeout` | 单次运行超时秒数 |
| `maxOutputBytes` | 最大捕获输出字节数 |

## 厂商 Headless 参考

### `cursor-cli`

厂商文档：[CLI Parameters](https://cursor.com/docs/cli/reference/parameters)、[Headless CLI](https://cursor.com/docs/cli/headless)。

DotCraft 内置参数：`--print --output-format json --mode ask --trust --approve-mcps`。无交互认证建议设置 `CURSOR_API_KEY`；当该变量存在时，DotCraft 会自动复制到子进程。

### `codex-cli`

厂商文档：[Codex CLI](https://github.com/openai/codex)、[`codex exec` reference](https://github.com/openai/codex/blob/main/docs/cli.md#codex-exec)。

DotCraft 内置参数：`exec --skip-git-repo-check`。返回结果默认走 `--output-last-message {path}` 输出文件契约，再由 DotCraft 读取最终文本。

## 在你的机器上检查 Profile

可直接调用 `InspectSubagentProfiles` 查看每个 profile 的 runtime、binary 解析结果、是否被提示词隐藏及具体原因。例如：

- `promptVisibility: visible` 表示可被模型使用
- `promptVisibility: hidden (binary 'cursor-agent' was not found on PATH)` 表示已自动隐藏

## 相关文档

- [配置指南](./config_guide.md)
- [Dashboard 使用指南](./dash_board_guide.md)
