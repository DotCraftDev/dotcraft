# DotCraft Settings Lifecycle Guide

This guide explains the three-tier settings lifecycle in Desktop, how API Proxy lock behavior works for LLM fields, and how to tell whether a change is already applied or still pending.

## 1. Three-Tier Settings Model

Desktop groups settings by how changes become effective:

1. **Live Apply (Tier A)**
   - Effective immediately after apply.
   - Typical examples: `Skills.DisabledSkills`, MCP configuration entries.
2. **Subsystem Restart (Tier B)**
   - Persisted, but requires restarting a related subsystem to take effect.
   - Typical examples: settings tied to proxy and external-channel subsystem lifecycle.
3. **Process Restart (Tier C)**
   - Persisted, but requires AppServer process restart to take effect.
   - Typical examples: `Core.ApiKey`, `Core.EndPoint`, `Core.Model`.

You can identify the tier from the group action pattern: immediate apply, restart, or apply-and-restart.

## 2. Representative Fields by Tier

| Area | Representative fields | Effect timing |
|---|---|---|
| Skills / MCP | `Skills.DisabledSkills`, MCP server definitions | Live Apply |
| Proxy / External Channel | Proxy and external channel related configuration | Subsystem Restart |
| Core (LLM) | `ApiKey`, `EndPoint`, `Model` | AppServer Restart |

Notes:

- The Desktop LLM group currently exposes `ApiKey` and `EndPoint`.
- `Model` still follows Tier C semantics but was removed from this Desktop group editor.

## 3. Proxy-Aware Lock Behavior

When managed API Proxy is running:

- `ApiKey` and `EndPoint` are locked to proxy-managed values.
- These two fields are read-only in Desktop.
- To restore manual editing, disable Proxy first, then run the corresponding apply/restart action so runtime and persisted state are aligned.

## 4. Applied vs Pending Changes

Use these signals to interpret state:

- **Applied**: the config is persisted and the required tier action is complete (live apply succeeded, or restart completed).
- **Pending**: restart-required hints are shown, which means config changed on disk but runtime has not switched yet.
- **Per-group dirty state**: if only one group changed, only that group's action is required; a global save is not needed.

## FAQ

**Q: I changed Model, why did nothing happen immediately?**  
A: `Model` is Tier C (process restart). The change is persisted first, and becomes active after AppServer restart.

For protocol details, see `workspace/configChanged` in the [AppServer Protocol](../../specs/appserver-protocol.md).
