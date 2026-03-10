# Module Audit

Snapshot of all modules used to derive the invariant rules in **module-development-spec.md**. Update this table when adding or changing modules so the spec and checklist stay accurate. Architecture and file paths are omitted; implementors discover them from the repository.

## Audit Table

| Module           | Host | Channel | Tools | Approval | Config key   | ValidateConfig |
|------------------|------|---------|-------|----------|--------------|----------------|
| cli              | Yes  | No      | No    | Console  | (always on)  | No             |
| api              | No   | Yes     | No    | API (interactive/auto) | Api | Yes            |
| acp              | Yes  | No      | No    | Acp (stdio) | Acp         | No             |
| gateway          | Yes  | No     | No    | Routes to channels | (derived) | Yes        |
| ag-ui            | No   | Yes     | No    | AutoApprove | AgUi       | Yes            |
| github-tracker   | No   | Yes     | No  | AutoApprove (runner) | GitHubTracker | Yes   |
| qq               | No   | Yes     | Yes   | Chat (QQApprovalService) | QQBot | Yes     |
| wecom            | No   | Yes     | Yes   | Chat (WeComApprovalService) | WeComBot | Yes  |
| unity            | No   | No      | Yes   | N/A (runs under ACP host) | Acp | No        |

## Invariant Rules

1. **One Host per process**: At most one Host runs per process. The host is selected by the primary module (highest priority among enabled modules that have an `IHostFactory`).

2. **Gateway is the only multi-channel Host**: Gateway is the only Host that runs multiple Channels. It collects `IChannelService` from all enabled non-gateway modules whose `CreateChannelService` returns non-null.

3. **Channel approval under Gateway**: Channels under Gateway expose approval via `IChannelService.ApprovalService`. Gateway builds a routing approval service that delegates to each channel's `ApprovalService` (or fallback) based on approval context.

4. **Tools are optional**: A module may return an empty list from `GetToolProviders()`. Tools are collected only from enabled modules.

5. **Config in Core**: All module configuration lives in Core's `AppConfig`. New modules add a config section there and implement `ValidateConfig` when the section has required fields.

6. **HITL for agent-running entry points**: Channels and Hosts that run the agent with sensitive tools (file, shell, etc.) must provide or route approval—either a full `IApprovalService` flow or documented use of AutoApprove where acceptable (e.g. AG-UI frontend, GitHubTracker autonomous runner).
