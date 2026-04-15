# DotCraft External CLI Subagent — M5: Git Worktree Isolation

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [M1: Runtime Abstraction](external-cli-subagent-m1.md), [M2: One-Shot CLI Runtime](external-cli-subagent-m2.md), [M4: Persistent CLI Runtime](external-cli-subagent-m4.md), [External CLI Subagent Design](external-cli-subagent-design.md) |

Purpose: Define the git worktree allocation, lifecycle management, diff review, and merge-back workflow for external code-writing subagents, providing filesystem isolation that protects the user's working tree from unreviewed changes while enabling agent-authored code to be inspected and applied deliberately.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Isolation Model](#3-isolation-model)
- [4. Worktree Manager](#4-worktree-manager)
- [5. Worktree Lifecycle](#5-worktree-lifecycle)
- [6. Profile Configuration for Isolation](#6-profile-configuration-for-isolation)
- [7. Diff Review Workflow](#7-diff-review-workflow)
- [8. Merge-Back Workflow](#8-merge-back-workflow)
- [9. Concurrent Worktree Management](#9-concurrent-worktree-management)
- [10. Integration with Runtimes](#10-integration-with-runtimes)
- [11. Constraints and Compatibility Notes](#11-constraints-and-compatibility-notes)
- [12. Acceptance Checklist](#12-acceptance-checklist)
- [13. Open Questions](#13-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The worktree isolation model: when and how a git worktree is created for an external subagent.
- The `SubAgentWorktreeManager` responsibilities: creation, registration, cleanup, and limit enforcement.
- The worktree lifecycle tied to the subagent session: creation on launch, preservation on completion, cleanup on disposal or timeout.
- The `workingDirectoryMode: "worktree"` profile configuration option and its interaction with the coordinator.
- The diff review workflow: how the main agent and the user inspect changes the external agent made.
- The merge-back workflow: how changes from the worktree are applied to the main working tree.
- Integration with both `CliOneshotRuntime` (M2) and `CliPersistentRuntime` (M4).
- Session events and tool surface for worktree state.

### 1.2 What This Spec Does Not Define

- Automatic merge without explicit user or agent review. Merge must be an intentional action.
- Multi-agent worktree conflict resolution (two subagents racing to merge changes).
- Complex branch strategies beyond temporary worktree branches.
- Worktree support for the native DotCraft subagent runtime. Worktrees are an external runtime feature.
- Trust policies for the merge-back action. Those are defined in the M6 spec.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Protect the user's working tree**: Code-writing external agents operate in a separate worktree branch, not in the user's active working directory. No changes reach the main working tree without an explicit merge action.
2. **Enable inspection before merge**: After the external agent completes, the main DotCraft agent can produce a structured diff of the changes and present it to the user before applying anything.
3. **Support both one-shot and persistent runtimes**: Worktree allocation and the review/merge workflow apply to both runtime types without requiring runtime-specific special cases.
4. **Deterministic cleanup**: Worktrees are cleaned up when sessions are disposed, on timeout, or on DotCraft process exit. Stale worktrees do not accumulate.
5. **Low setup friction**: Users enable worktree isolation via a single profile config change (`workingDirectoryMode: "worktree"`). No manual git setup is required.

### 2.2 Non-Goals

- Rebasing or squashing commits made by the external agent before merge.
- Supporting worktrees for repositories that do not use git.
- Protecting against the external agent escaping the worktree directory using absolute paths or symlinks (this is a trust boundary concern addressed in M6 security policy, not a git-level enforcement).
- Providing a visual diff editor in the DotCraft clients. Diff output is text-based in this milestone.

---

## 3. Isolation Model

### 3.1 One Worktree Per External Subagent Session

Each external subagent session with `workingDirectoryMode: "worktree"` receives its own git worktree:

- The worktree is created from the current `HEAD` of the repository.
- The worktree is placed in a coordinator-managed directory (default: `.craft/worktrees/<agentId>/`).
- The external agent's working directory is set to the root of the worktree.
- Changes made by the external agent accumulate in the worktree's working tree and index.
- The main repository's working tree is not touched until an explicit merge action.

### 3.2 Worktree Branch Naming

Each worktree is created on a new local branch:

```
craft/subagent/<agentId>/<timestamp>
```

This branch is created from `HEAD` at session launch time. The branch name is deterministic (no random suffix) to allow easy identification and cleanup.

### 3.3 Non-Git Workspace Fallback

If the workspace is not a git repository, `workingDirectoryMode: "worktree"` is not supported. The coordinator:
- Returns an error result if `workingDirectoryMode: "worktree"` is explicitly set in the profile.
- Falls back to `workingDirectoryMode: "workspace"` if the profile uses `"worktree"` as a default and the workspace is not a git repo, with a warning in the event sink.

---

## 4. Worktree Manager

### 4.1 Responsibilities

`SubAgentWorktreeManager` is a coordinator-level component that:

- Creates git worktrees via `git worktree add` on request.
- Tracks active worktrees in an in-memory registry keyed by `agentId`.
- Enforces the concurrent worktree limit.
- Removes worktrees via `git worktree remove --force` on cleanup.
- Runs a background cleanup sweep for stale worktrees.
- Exposes the worktree path and branch name to the coordinator for use as the subprocess working directory.

### 4.2 Git Command Execution

All git operations are performed by invoking `git` as a subprocess using the same pattern as the existing `CommitMessageSuggestService`. The `git` binary must be available on `PATH`. If it is not, worktree operations fail with a clear error.

### 4.3 Worktree Registry

The registry maintains `WorktreeEntry` records:

| Field | Type | Description |
|-------|------|-------------|
| `AgentId` | string | Session identifier |
| `Branch` | string | Full branch name |
| `Path` | string | Absolute path to the worktree root |
| `CreatedAt` | DateTimeOffset | Creation timestamp |
| `Status` | enum | `Active`, `PendingReview`, `Merged`, `Disposed` |

---

## 5. Worktree Lifecycle

### 5.1 Creation

Triggered by `SubAgentCoordinator` before calling `CreateSessionAsync` when the resolved working directory mode is `worktree`:

1. Determine the branch name and worktree path.
2. Run `git worktree add <path> -b <branch> HEAD`.
3. Register the `WorktreeEntry` with `Status: Active`.
4. Pass the worktree path to the runtime as its working directory.

If `git worktree add` fails (e.g., conflicting branch name, disk error), the coordinator returns an error result and does not start the subprocess.

### 5.2 Active Phase

While the session is running:
- The external agent may create, modify, and delete files in the worktree.
- The external agent may commit to the worktree branch.
- DotCraft does not monitor the worktree's index or working tree during this phase.

### 5.3 Completion and Pending Review

When `RunAsync` returns (for one-shot) or when `DisposeSessionAsync` is called (for persistent):
- The `WorktreeEntry.Status` is set to `PendingReview`.
- The worktree is **not** removed at this point.
- An `ExternalSubAgentWorktreeReady` session event is emitted with the worktree path and branch name.
- The main agent receives the worktree path and branch as part of the tool result or in a separate event, so it can invoke diff and merge tools.

### 5.4 Review Phase

In `PendingReview` status, the worktree is available for inspection:
- The main agent or the user can request a diff via the `ReviewSubagentWorktree` tool (see §7).
- The user or the main agent can request a merge via the `MergeSubagentWorktree` tool (see §8).
- The worktree remains on disk until explicitly merged, disposed, or cleaned up by the stale sweep.

### 5.5 Disposal and Cleanup

The worktree is removed in any of these conditions:
- `MergeSubagentWorktree` is called (merge-back completes; worktree removed after merge).
- `CancelSubagent` is called with `discardChanges: true` [M6].
- The stale sweep detects that the worktree has been in `PendingReview` for longer than `subAgentWorktreeReviewTimeout` (default: 24 hours) and removes it automatically.
- DotCraft exits; all worktrees in `Active` status are forcibly removed.

Removal sequence:
1. Run `git worktree remove --force <path>`.
2. Delete the worktree branch with `git branch -D <branch>` if it has not been merged.
3. Remove the `WorktreeEntry` from the registry.

---

## 6. Profile Configuration for Isolation

### 6.1 `workingDirectoryMode`

Setting `workingDirectoryMode: "worktree"` in a profile enables worktree isolation for all sessions created with that profile. This is the recommended value for code-writing external agents.

| `workingDirectoryMode` | Working directory | Worktree created |
|-----------------------|-------------------|-----------------|
| `workspace` | Workspace root | No |
| `specified` | Caller-provided path | No |
| `worktree` | New git worktree root | Yes |

### 6.2 `supportsWorktree`

A profile declares `supportsWorktree: true` to indicate that the external CLI can operate correctly in a git worktree (i.e., it respects the working directory and does not hardcode paths to the main workspace). If `supportsWorktree: false` and `workingDirectoryMode: "worktree"` is set, the coordinator returns a configuration error.

### 6.3 Default for Code-Writing Profiles

The built-in `claude-code` (both oneshot and persistent) profiles set `workingDirectoryMode: "worktree"` as the default, reflecting that these agents are designed to write code and should be isolated by default.

Users who understand the risks can override this to `workspace` in their config.

---

## 7. Diff Review Workflow

### 7.1 `ReviewSubagentWorktree` Tool

The `ReviewSubagentWorktree` tool allows the main agent to inspect the changes made by an external subagent before deciding to merge or discard them.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `agentId` | string | yes | The subagent session whose worktree to review |
| `diffMode` | string | no | `stat` (file list with change counts), `full` (full unified diff), `summary` (AI-friendly condensed diff). Default: `stat` |
| `pathFilter` | string | no | Glob pattern to limit diff to specific files |

**Behavior:**

1. Look up the `WorktreeEntry` for `agentId`.
2. Run `git diff HEAD <branch>` (or `git diff --stat`) in the worktree.
3. Return the diff output as the tool result, truncated at the output size limit with a truncation marker if needed.

**Output size:**

- `stat` mode: typically small; no truncation expected.
- `full` mode: may be large; capped at 200 KB by default, with a message indicating how many lines were omitted.
- `summary` mode: the coordinator summarizes the `stat` output and lists key changed files with line delta counts.

### 7.2 Unsupported State

`ReviewSubagentWorktree` returns an error if:
- The `agentId` is not found in the worktree registry.
- The worktree `Status` is `Disposed` or `Merged`.
- The worktree directory does not exist (e.g., was cleaned up externally).

---

## 8. Merge-Back Workflow

### 8.1 `MergeSubagentWorktree` Tool

The `MergeSubagentWorktree` tool applies changes from the worktree to the main working tree. This is an explicit, intentional action.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `agentId` | string | yes | The subagent session whose worktree to merge |
| `strategy` | string | no | `patch` (apply as unstaged changes), `commit` (cherry-pick commits), `merge` (merge the worktree branch). Default: `patch` |
| `commitMessage` | string | no | Used with `strategy: "commit"` to override the commit message |

**Strategies:**

| Strategy | Behavior |
|----------|---------|
| `patch` | Run `git diff HEAD <branch>` in the worktree and apply the resulting patch to the main working tree as unstaged changes. The user retains full control over staging and committing. |
| `commit` | Cherry-pick all commits on the worktree branch onto the current `HEAD`. Commit authorship is preserved; the commit message can be overridden. |
| `merge` | Run `git merge <branch>` in the main working tree. Merge commits include all worktree commits. Conflicts are left for the user to resolve. |

**Post-merge cleanup:**

After a successful merge (all strategies except `merge` with conflicts):
1. Set `WorktreeEntry.Status` to `Merged`.
2. Remove the worktree via `git worktree remove --force`.
3. Delete the worktree branch via `git branch -D`.
4. Emit `ExternalSubAgentWorktreeMerged` session event.

If the merge produces conflicts (`merge` strategy):
- The worktree is not removed.
- `WorktreeEntry.Status` remains `PendingReview`.
- The tool result describes the conflicts.
- The user must resolve conflicts manually before the worktree can be cleaned up via `CancelSubagent` [M6].

### 8.2 Discard Workflow

The main agent may instruct the user to discard the worktree changes instead of merging. This is done via `CancelSubagent` with `discardChanges: true` [M6], which removes the worktree and its branch without applying any changes to the main working tree.

---

## 9. Concurrent Worktree Management

### 9.1 Concurrent Worktree Limit

The number of active worktrees is bounded by `SubagentMaxWorktrees` (default: 5, configurable). Creating a worktree when the limit is reached returns an error result. The main agent must clean up an existing worktree (via merge or discard) before a new one can be created.

### 9.2 Stale Worktree Sweep

A background sweep runs every 10 minutes:
- Worktrees in `Active` status belonging to disposed sessions are removed.
- Worktrees in `PendingReview` status older than `subAgentWorktreeReviewTimeout` are removed (changes are discarded with a warning event).
- Worktrees in `Merged` status that were not yet cleaned up are removed.

### 9.3 DotCraft Restart Recovery

On DotCraft startup, the coordinator scans `.craft/worktrees/` for directories left from a previous session:
- Any found worktrees are registered in the registry with `Status: PendingReview`.
- An `ExternalSubAgentWorktreeRecovered` session event is emitted for each recovered worktree.
- The main agent may then review and merge or discard them as normal.
- If the associated git worktree is no longer valid (e.g., `.git/worktrees/` entry was pruned), the directory is cleaned up and skipped.

---

## 10. Integration with Runtimes

### 10.1 One-Shot Runtime (M2)

- The coordinator calls `SubAgentWorktreeManager.CreateAsync` before `CliOneshotRuntime.CreateSessionAsync`.
- The resolved worktree path is passed to `SubAgentTaskRequest.WorkingDirectory`.
- After `RunAsync` returns, the worktree is transitioned to `PendingReview`.
- The tool result includes the `agentId` with a note that the worktree is ready for review.

### 10.2 Persistent Runtime (M4)

- The worktree is created when the persistent session is first established (during `CreateSessionAsync`).
- The same worktree is used for all turns in the persistent session.
- The worktree transitions to `PendingReview` when the session is disposed or when the main agent calls `ReviewSubagentWorktree`.
- `SendSubagentInput` turns do not reallocate the worktree.

### 10.3 Event Integration (M3)

New session event types for worktree state:

| Event type | When emitted |
|------------|-------------|
| `ExternalSubAgentWorktreeCreated` | After `git worktree add` succeeds |
| `ExternalSubAgentWorktreeReady` | When worktree transitions to `PendingReview` |
| `ExternalSubAgentWorktreeMerged` | After successful merge-back |
| `ExternalSubAgentWorktreeDiscarded` | After worktree is removed without merging |
| `ExternalSubAgentWorktreeRecovered` | After DotCraft restart finds a previous worktree |

---

## 11. Constraints and Compatibility Notes

- Worktree creation requires `git` to be installed and the workspace to be a git repository. Both conditions are checked before attempting worktree creation; missing `git` or a non-git workspace produces a clear error.
- The worktree directory path (`.craft/worktrees/`) should be added to `.gitignore` to prevent the worktree container from appearing as an untracked file in the main repository.
- The coordinator must check that the worktree branch name does not conflict with existing local branches before running `git worktree add`. If a conflict exists, a numeric suffix is appended.
- External agents that manipulate `git remote` or `git push` from within the worktree are not prevented from doing so by DotCraft. This is a trust boundary concern addressed in M6.
- `SubAgentWorktreeManager` must not depend on `SubAgentManager` or any native runtime code. It is a standalone component used by the coordinator.

---

## 12. Acceptance Checklist

- [ ] `SubAgentWorktreeManager` creates a git worktree via `git worktree add` correctly.
- [ ] Worktree is placed at `.craft/worktrees/<agentId>/` by default.
- [ ] Worktree branch is named `craft/subagent/<agentId>/<timestamp>`.
- [ ] Branch name conflict detection and numeric suffix appending work correctly.
- [ ] Non-git workspace returns a clear error when `workingDirectoryMode: "worktree"` is requested.
- [ ] `WorktreeEntry` status transitions are correct for the full lifecycle.
- [ ] `ExternalSubAgentWorktreeCreated` and `ExternalSubAgentWorktreeReady` events are emitted at correct points.
- [ ] `ReviewSubagentWorktree` tool returns correct diff output for all three modes (`stat`, `full`, `summary`).
- [ ] `ReviewSubagentWorktree` returns an error for disposed or not-found worktrees.
- [ ] `MergeSubagentWorktree` with `strategy: "patch"` applies changes as unstaged diffs in the main working tree.
- [ ] `MergeSubagentWorktree` with `strategy: "commit"` cherry-picks commits onto `HEAD`.
- [ ] `MergeSubagentWorktree` with `strategy: "merge"` merges the branch.
- [ ] Post-merge cleanup removes the worktree and branch after success.
- [ ] `ExternalSubAgentWorktreeMerged` event is emitted after successful merge.
- [ ] Stale worktree sweep removes `PendingReview` worktrees older than the configured timeout.
- [ ] DotCraft restart recovery detects and registers previous worktrees.
- [ ] `SubagentMaxWorktrees` limit is enforced; excess requests return a clear error.
- [ ] `.craft/worktrees/` is added to `.gitignore` as part of the feature delivery.
- [ ] Integration works end-to-end with both `CliOneshotRuntime` and `CliPersistentRuntime`.

---

## 13. Open Questions

1. Should `.craft/worktrees/` be the default worktree container, or should it be configurable? (Preference: configurable with `.craft/worktrees/` as the default.)
2. Should the worktree branch be automatically pushed to the remote after merge for traceability, or kept local only? (Preference: local only in M5; remote push is an M6+ feature.)
3. Should the `summary` diff mode summarize using an LLM call or be a purely algorithmic abbreviation of `git diff --stat`? (Preference: algorithmic in M5 to avoid adding a model call in the merge workflow path.)
4. When `MergeSubagentWorktree` with `strategy: "merge"` produces conflicts, should DotCraft attempt to auto-resolve any conflicts, or always leave them for the user?
5. Should `workingDirectoryMode: "worktree"` be the default for all code-writing external profiles, or should the user opt in explicitly?
6. Should the worktree creation step emit an approval request [M6] that the user must confirm before the subprocess is launched?
