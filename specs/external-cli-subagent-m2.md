# DotCraft External CLI Subagent — M2: One-Shot CLI Runtime

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [M1: Runtime Abstraction](external-cli-subagent-m1.md), [External CLI Subagent Design](external-cli-subagent-design.md) |

Purpose: Define the behavior, lifecycle, output parsing contract, and built-in profiles for the one-shot CLI runtime — the first external subagent execution path — which launches a third-party CLI agent as a subprocess per delegated task and returns its output as a tool result to the main agent.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. One-Shot Execution Model](#3-one-shot-execution-model)
- [4. Process Lifecycle](#4-process-lifecycle)
- [5. Input Contract](#5-input-contract)
- [6. Output Parsing Contract](#6-output-parsing-contract)
- [7. Error Handling](#7-error-handling)
- [8. Working Directory Behavior](#8-working-directory-behavior)
- [9. Built-In Profiles](#9-built-in-profiles)
- [10. Custom Profile Support](#10-custom-profile-support)
- [11. Constraints and Compatibility Notes](#11-constraints-and-compatibility-notes)
- [12. Acceptance Checklist](#12-acceptance-checklist)
- [13. Open Questions](#13-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The `CliOneshotRuntime` implementation of `ISubAgentRuntime` for per-task subprocess execution.
- The subprocess launch lifecycle: binary resolution, argument construction, environment setup, process start, and teardown.
- The input contract: how the task instruction is passed to the subprocess (stdin, argument, or env variable).
- The output parsing contract: structured JSON parsing when available; raw text fallback when not.
- Error handling rules: timeout, non-zero exit, binary not found, and permission errors.
- Working directory resolution for one-shot invocations.
- Built-in profiles: `claude-code` (oneshot mode), `codex-cli`, and `custom-cli-oneshot`.
- Deferred research gates before finalizing CLI-specific argument formats.

### 1.2 What This Spec Does Not Define

- Rich progress streaming of stdout/stderr into session events. That is defined in the M3 spec.
- Persistent or multi-turn subprocess communication. That is defined in the M4 spec.
- Git worktree allocation for subprocess isolation. That is defined in the M5 spec.
- Trust levels and launch approval gates. Those are defined in the M6 spec.
- Token accounting extracted from subprocess output. That is defined in the M3 spec as part of the event sink.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Minimal viable external delegation**: The main agent can instruct a named external CLI tool to perform a task and receive its final output, without requiring persistent state or streaming infrastructure.
2. **Structured output preference**: Where the external CLI supports a machine-readable output mode (e.g., `--json`, `--output-format json`), the profile should configure it, and the runtime should parse it. Raw text capture is the fallback, not the target.
3. **Robust process lifecycle**: The runtime must handle process startup failures, timeouts, and non-zero exits with predictable, useful error messages that the main agent can reason about.
4. **Profile-driven, not hardcoded**: All CLI-specific arguments, environment variables, and parsing rules are captured in the profile. `CliOneshotRuntime` does not contain hardcoded knowledge of any specific vendor's CLI.
5. **Safe by default**: The subprocess inherits a minimal environment. Sensitive environment variables from the DotCraft process are not forwarded unless explicitly listed in the profile's `env` section.

### 2.2 Non-Goals

- Streaming incremental output to the user during subprocess execution (only the final result is returned in M2).
- Supporting multi-turn back-and-forth with a running external agent.
- Isolating the subprocess in a separate git worktree (the subprocess runs in the workspace directory or a caller-specified directory).
- Implementing vendor-specific rich tool event parsing (file edits, shell commands, etc.) from the subprocess output.
- Authenticating to external CLI tools on behalf of the user. Credential setup is a user responsibility.

---

## 3. One-Shot Execution Model

A one-shot invocation follows this sequence:

1. The main agent calls `SpawnSubagent(task: "...", profile: "codex-cli")`.
2. `SubAgentCoordinator` resolves the `codex-cli` profile and selects `CliOneshotRuntime`.
3. `CreateSessionAsync` returns an opaque handle immediately (no resource allocation needed for one-shot).
4. `RunAsync` launches the subprocess with the task as input, waits for exit, collects stdout/stderr, and returns `SubAgentRunResult`.
5. `DisposeSessionAsync` ensures the process handle is released.
6. The coordinator returns the result text to `SpawnSubagent`, which passes it back to the main agent as a tool result.

Each `SpawnSubagent` invocation starts a fresh subprocess. There is no session state shared between invocations for the same profile in the one-shot model.

---

## 4. Process Lifecycle

### 4.1 Binary Resolution

The runtime resolves the executable in this order:

1. If `bin` is an absolute path, use it directly (fail fast if it does not exist).
2. If `bin` is a relative path or bare command name, search the `PATH` environment variable as it exists in the DotCraft process (not the subprocess's inherited environment).
3. If the binary is not found, return an error result with a clear message indicating the binary name and that it must be installed and available on `PATH`.

### 4.2 Environment Construction

The subprocess environment is constructed as follows:

- Start with a minimal base: OS-required variables only (e.g., `COMSPEC`/`SHELL`, `HOME`, `USERPROFILE`, `SystemRoot`, `PATH`).
- Apply any key-value pairs from `profile.env`, which may add, override, or explicitly clear variables.
- Do not forward DotCraft-specific variables (e.g., `DOTCRAFT_*`, API keys read by DotCraft's own config) unless they appear in `profile.env`.

### 4.3 Argument Construction

Arguments are constructed as:

```
[profile.args] [task-delivery-args]
```

Where `task-delivery-args` is determined by the profile's `inputFormat` and `inputMode` settings (see §5).

### 4.4 Process Execution

- The process is started with `redirectStandardOutput: true`, `redirectStandardError: true`, `useShellExecute: false`.
- Standard input is opened for writing if `inputMode` is `stdin`.
- Stdout and stderr are captured in full before the result is returned.
- The process is killed (and its child tree, where the OS supports it) if the `timeout` elapses.

### 4.5 Shutdown and Cleanup

After `RunAsync` returns or throws:

- The process handle is closed.
- If the process is still running (e.g., after a timeout), a kill signal is sent before the handle is released.
- `DisposeSessionAsync` is a no-op for the one-shot runtime but must be called consistently for interface conformance.

---

## 5. Input Contract

### 5.1 Input Modes

The profile's `inputMode` field determines how the task instruction is delivered to the subprocess:

| `inputMode` | Behavior |
|-------------|----------|
| `stdin` | Task text is written to the process's standard input, then stdin is closed. |
| `arg` | Task text is appended as the final positional argument. |
| `arg-template` | Task text is interpolated into a template string specified in `inputArgTemplate`. |
| `env` | Task text is injected as an environment variable named by `inputEnvKey`. |

Default `inputMode` when not specified: `arg`.

### 5.2 Input Encoding

Task text is UTF-8 encoded. No additional escaping is applied beyond what the OS requires for the chosen delivery mode. Implementations must not truncate task text; if the OS imposes argument length limits, `stdin` should be used instead of `arg`.

---

## 6. Output Parsing Contract

### 6.1 Output Mode Selection

The profile's `outputFormat` field determines parsing behavior:

| `outputFormat` | Behavior |
|----------------|----------|
| `text` | Stdout is returned as-is as `SubAgentRunResult.Text`. Stderr is appended with a separator if non-empty and the exit code is non-zero. |
| `json` | Stdout is parsed as JSON. The result text is extracted from the field path specified in `outputJsonPath`. If parsing fails, the runtime falls back to raw text and sets a warning in the result. |

Default `outputFormat` when not specified: `text`.

### 6.2 JSON Output Field Extraction

When `outputFormat` is `json`, the profile must also specify `outputJsonPath`: a dot-separated path to the string field containing the agent's response (e.g., `result.message` or `content`).

If the field is absent or the JSON is malformed:
- The runtime falls back to the raw stdout string as `SubAgentRunResult.Text`.
- `SubAgentRunResult.IsError` is set to `false` (the agent completed; parsing failed).
- A structured warning is included in the result text indicating the parsing failure.

### 6.3 Stderr Handling

Stderr is always captured but its disposition depends on exit code:
- Exit code 0: stderr is discarded (may contain diagnostic noise from well-behaved CLIs).
- Non-zero exit code: stderr is included in the error result alongside stdout.

### 6.4 Output Size Limits

The runtime caps captured output at a configurable limit (default 1 MB). If the limit is exceeded, the output is truncated and a truncation marker is appended. The result is returned with `IsError: false` to allow the main agent to interpret partial output.

---

## 7. Error Handling

### 7.1 Error Categories and Result Behavior

| Error | `IsError` | Result text |
|-------|-----------|-------------|
| Binary not found | true | Clear message with binary name and install hint |
| Process failed to start (OS error) | true | OS error message |
| Timeout elapsed | true | Message indicating elapsed time and configured timeout |
| Non-zero exit code | true | Exit code, captured stdout (if any), and stderr |
| JSON parse failure on `json` output format | false | Warning prepended to raw stdout |
| Output size limit exceeded | false | Truncated stdout with truncation marker |

### 7.2 Error Propagation

`RunAsync` must not throw for recoverable subprocess errors (non-zero exit, timeout, parse failure). These are returned as `SubAgentRunResult` with `IsError: true` so the coordinator can convert them to a tool result string without crashing the parent turn.

Unrecoverable errors (unexpected `IOException`, platform error during process management) may throw and will be caught by the coordinator, which will return an `"Error: ..."` tool result matching the existing native subagent error pattern.

---

## 8. Working Directory Behavior

The subprocess working directory is set to the value resolved by `SubAgentCoordinator` for the invocation (see M1 §6.4):

- `workspace` (default): the workspace root.
- `specified`: a directory path passed by the main agent in the `SpawnSubagent` call, validated to exist and be accessible.
- `worktree`: a coordinator-allocated git worktree path [M5].

The runtime does not perform additional path validation beyond confirming that the resolved directory exists before starting the process.

---

## 9. Built-In Profiles

The following profiles are registered as built-in defaults in M2. User-configured profiles with the same name override them.

### 9.1 `claude-code` (oneshot)

> **Note:** Exact argument flags are subject to change pending implementation-time research into the Claude Code CLI automation surface. The values below represent the intended design direction; they must be validated against the actual CLI before committing.

| Field | Value |
|-------|-------|
| `runtime` | `cli-oneshot` |
| `bin` | `claude` |
| `args` | `["--print", "--output-format", "json"]` |
| `inputMode` | `stdin` |
| `outputFormat` | `json` |
| `outputJsonPath` | `result` |
| `supportsStreaming` | false |
| `supportsResume` | false |
| `timeout` | 300 |

### 9.2 `codex-cli`

> **Note:** Same caveat as above — argument flags must be validated against the Codex CLI during implementation.

| Field | Value |
|-------|-------|
| `runtime` | `cli-oneshot` |
| `bin` | `codex` |
| `args` | `["--full-auto", "--quiet"]` |
| `inputMode` | `arg` |
| `outputFormat` | `text` |
| `supportsStreaming` | false |
| `supportsResume` | false |
| `timeout` | 300 |

### 9.3 `custom-cli-oneshot`

A template profile for user-defined one-shot CLIs. All fields except `runtime` must be provided by the user in their config.

| Field | Value |
|-------|-------|
| `runtime` | `cli-oneshot` |
| `bin` | *(required)* |
| `args` | `[]` |
| `inputMode` | `arg` |
| `outputFormat` | `text` |
| `timeout` | 120 |

---

## 10. Custom Profile Support

Users can define additional one-shot CLI profiles in workspace or global config under `SubAgentProfiles`. Any profile with `"runtime": "cli-oneshot"` is routed to `CliOneshotRuntime`. Required fields: `bin`. All other fields have defaults as specified in §5 and §6.

Custom profiles may override any built-in profile name. This allows users to tune `claude-code` or `codex-cli` argument flags for their local installation without forking the profile name.

---

## 11. Constraints and Compatibility Notes

- `CliOneshotRuntime` must be isolated from `NativeSubAgentRuntime`. The two runtimes share the `ISubAgentRuntime` interface but no implementation code.
- One-shot invocations do not consume the native subagent concurrency gate (`SubagentMaxConcurrency`). If concurrent limits are desired for external CLIs, they must be declared as a separate limit in M6.
- The parent turn's `CancellationToken` must be passed to the process wait. If the turn is cancelled, the subprocess is killed and `RunAsync` returns an error result rather than throwing `OperationCanceledException` to the coordinator.
- The `SpawnSubagent` tool description seen by the main agent must not claim that external profiles support all native subagent capabilities (e.g., it must not suggest that `codex-cli` can produce real-time streamed tool call previews).

---

## 12. Acceptance Checklist

- [ ] `CliOneshotRuntime` implements `ISubAgentRuntime` and is registered in the coordinator for `runtime: "cli-oneshot"`.
- [ ] Binary resolution searches PATH correctly; absent binary produces a clear error result.
- [ ] Environment construction uses the minimal base and applies `profile.env` overrides; DotCraft-internal variables are not leaked.
- [ ] Argument construction respects `profile.args` and all four `inputMode` variants.
- [ ] `text` output format returns raw stdout correctly.
- [ ] `json` output format parses the field at `outputJsonPath` and falls back to raw text with a warning on failure.
- [ ] Stderr is captured and included in error results for non-zero exits.
- [ ] Output size limiting and truncation marker work correctly.
- [ ] Timeout fires correctly and kills the process tree; result is `IsError: true` with elapsed time.
- [ ] Cancellation of the parent turn kills the subprocess and returns an error result.
- [ ] Working directory is set to the coordinator-resolved path.
- [ ] Built-in profiles `claude-code`, `codex-cli`, and `custom-cli-oneshot` are registered.
- [ ] User-defined profiles with `"runtime": "cli-oneshot"` route correctly to `CliOneshotRuntime`.
- [ ] End-to-end test: `SpawnSubagent(profile: "custom-cli-oneshot", ...)` with a real trivial CLI (e.g., `echo`) returns the expected output as a tool result.
- [ ] Research gate: built-in `claude-code` and `codex-cli` argument flags have been validated against the actual CLIs before the profile is shipped as a default.

---

## 13. Open Questions

1. Should the output size limit be a global config value or per-profile? (Preference: both — global default with per-profile override.)
2. Should `CliOneshotRuntime` have its own concurrency limit separate from the native subagent gate?
3. Should the `claude-code` oneshot profile send the task via stdin only, or also support an initial system prompt argument? This depends on the actual Claude Code CLI automation surface.
4. If the external CLI produces a mix of structured and unstructured output (e.g., progress lines followed by a JSON block), should the runtime strip non-JSON prefix lines before parsing? (This may require a `jsonExtractMode: "last-block"` option in the profile.)
5. Should validation of `bin` existence be performed at profile load time or deferred to runtime invocation?
