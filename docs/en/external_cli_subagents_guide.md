# External CLI SubAgents Guide

External CLI subagents let you run an existing coding-agent CLI as a DotCraft one-shot subagent. Compared with `native`, external CLIs typically expose stage-level progress rather than tool-by-tool execution details.

## Built-in Profiles

| Name | Runtime | Default `bin` | Headless entry point |
|------|---------|---------------|----------------------|
| `native` | `native` | - | DotCraft native runtime |
| `codex-cli` | `cli-oneshot` | `codex` | `codex exec` |
| `cursor-cli` | `cli-oneshot` | `cursor-agent` | `cursor-agent --print --output-format json --mode ask` |
| `custom-cli-oneshot` | `cli-oneshot` | - | Template profile; override with same name and set `bin` to make it usable |

If a `cli-oneshot` profile binary cannot be resolved on your machine, DotCraft hides that profile from the system prompt automatically and logs the reason at startup (e.g. `binary 'cursor-agent' was not found on PATH`).

## Quick Configure

Profiles are configured in `config.json` under `SubAgentProfiles`.

- Global: `~/.craft/config.json`
- Workspace: `<workspace>/.craft/config.json`

Workspace config overrides global profiles with the same name.

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

## Field Reference

| Field | Description |
|------|-------------|
| `runtime` | Runtime type. External one-shot profiles use `cli-oneshot` |
| `bin` | Executable name or absolute path |
| `args` | Fixed argument list |
| `workingDirectoryMode` | `workspace` / `specified` |
| `inputMode` | `stdin` / `arg` / `arg-template` / `env` |
| `inputArgTemplate` | Argument template used by `arg-template` mode |
| `inputEnvKey` | Environment variable key used by `env` mode |
| `env` | Fixed environment variables injected into the subprocess |
| `envPassthrough` | Environment variable names copied from the parent process |
| `outputFormat` | `text` or `json` |
| `outputJsonPath` | Final-result extraction path in JSON mode |
| `readOutputFile` | Prefer output-file content as final result |
| `outputFileArgTemplate` | Output-file argument template, supports `{path}` |
| `timeout` | Per-run timeout in seconds |
| `maxOutputBytes` | Maximum captured output bytes |

## Vendor Headless References

### `cursor-cli`

Vendor docs: [CLI Parameters](https://cursor.com/docs/cli/reference/parameters), [Headless CLI](https://cursor.com/docs/cli/headless).

DotCraft ships `cursor-cli` with `--print --output-format json --mode ask --trust --approve-mcps`. For non-interactive auth, set `CURSOR_API_KEY`; DotCraft copies it to the subprocess when present.

### `codex-cli`

Vendor docs: [Codex CLI](https://github.com/openai/codex), [`codex exec` reference](https://github.com/openai/codex/blob/main/docs/cli.md#codex-exec).

DotCraft invokes `codex-cli` as `codex exec "<task>"`, plus `--skip-git-repo-check --json --output-last-message {path}` for the output-file contract. When resume is enabled and a prior external session exists, DotCraft switches to `codex exec resume {sessionId} "<task>"`. Interactive and restricted launches add `--sandbox read-only`; auto-approve launches add `--dangerously-bypass-approvals-and-sandbox`.

For authentication, DotCraft automatically passes through `CODEX_API_KEY` and `OPENAI_API_KEY` to the subprocess. If you use `codex login`, its auth state is also reused via `~/.codex` under `HOME`/`USERPROFILE`.

## Inspect Profiles On Your Machine

DotCraft validates every profile at startup and prints the hidden built-in profiles (with the concrete reason) to the log, so you can quickly tell whether a profile is missing `bin`, whether the binary is not on `PATH`, or whether a required field is misconfigured. Check the startup log to know which profiles are visible to the model.
