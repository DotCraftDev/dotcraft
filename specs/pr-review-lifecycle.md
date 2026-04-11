# DotCraft PR Review Lifecycle Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-19 |
| **Parent Spec** | Symphony SPEC (GitHubTracker Orchestrator §7–8) |

Defines the automated pull-request review lifecycle for the GitHubTracker module — from PR discovery through agent review to automatic re-review on new commits, using polling-based HEAD SHA tracking.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. PR Discovery](#2-pr-discovery)
- [3. Head SHA Tracking](#3-head-sha-tracking)
- [4. PR Review Lifecycle State Machine](#4-pr-review-lifecycle-state-machine)
- [5. Orchestrator Dispatch Rules](#5-orchestrator-dispatch-rules)
- [6. Agent Review Behavioral Contract](#6-agent-review-behavioral-contract)
- [7. Post-Review Completion Flow](#7-post-review-completion-flow)
- [8. Configuration](#8-configuration)

---

## 1. Scope

This spec defines:

- The candidate selection strategy for pull requests eligible for automated review.
- The HEAD SHA tracking mechanism that enables re-review when new commits are pushed.
- The complete PR review lifecycle state machine within the orchestrator's in-memory state.
- The behavioral constraint on the `SubmitReview` tool (COMMENT-only policy).
- The post-completion flow in the orchestrator.

This spec does not define:

- The general orchestrator reconciliation loop, poll tick mechanics, retry queue, or stall detection. Those are governed by the Symphony SPEC (§7–8) and remain unchanged.
- The issue tracking lifecycle. Issue discovery, dispatch, and completion are unaffected.
- PR diff fetching, workspace provisioning, or agent execution mechanics.

---

## 2. PR Discovery

### 2.1 Candidate Selection

On each poll tick, the adapter fetches all open pull requests and applies the following filters in order:

| Step | Filter | Action |
|------|--------|--------|
| 1 | `draft == true` | Exclude — draft PRs are never candidates |
| 2 | `PullRequestActiveStates` | Keep only PRs whose derived state matches a configured active state |

All open, non-draft PRs in an active review state are automatically eligible. No label gate is applied.

### 2.2 Head SHA Mapping

Every `TrackedWorkItem` of kind `PullRequest` must have its `HeadSha` field populated from the GitHub API response (`head.sha`). This field is `null` for issues.

---

## 3. Head SHA Tracking

### 3.1 Data Model

`TrackedWorkItem` carries a `HeadSha` field representing the HEAD commit SHA of the pull request at fetch time.

The orchestrator's in-memory state maintains a `ReviewedSha` dictionary that maps each PR's work-item ID to the SHA at which it was last reviewed.

### 3.2 ReviewedSha Lifecycle

| Event | Action on ReviewedSha |
|-------|-----------------------|
| Review submitted successfully (`ReviewSubmitted == true`) | Record `ReviewedSha[prId] = headSha` |
| Running PR reaches a terminal state (detected via `ReconcileAsync` running-entry loop) | Remove `ReviewedSha[prId]` |
| Completed PR later reaches a terminal state (detected via `CleanupTerminalReviewedShaAsync` on each reconcile tick) | Remove `ReviewedSha[prId]` |
| Orchestrator restart | Dictionary is empty (in-memory only); all open PRs are reviewed once on the first poll tick |

Terminal-state cleanup runs on every reconcile tick regardless of whether any agents are currently running. IDs in `Running` or `Claimed` are excluded from the cleanup pass to avoid races with active agent sessions. Missing snapshots (PR not found by the tracker) are treated conservatively: the entry is kept.

The in-memory approach is intentional. The cost of an occasional redundant review after restart is acceptable, and it avoids persistent state management complexity. This is consistent with the existing `Claimed`, `Running`, and `Completed` sets, which are also in-memory only.

---

## 4. PR Review Lifecycle State Machine

```
                                   ┌────────────────────────────────────────┐
                                   │          SHA Changed (new push)        │
                                   │                                        │
┌────────────┐   ShouldDispatch   ┌┴───────────┐   Dispatch   ┌────────────┴─┐
│            │──────────────────► │            │────────────►  │              │
│ Candidate  │                    │  Claimed   │               │   Running    │
│            │◄── not eligible    │            │               │  (Agent run) │
└────────────┘  (SHA unchanged)   └────────────┘               └──────┬───────┘
      ▲                                 ▲                             │
      │                                 │                ┌────────────┴────────────┐
      │                                 │                │                         │
      │                                 │     ReviewSubmitted == true    Turns exhausted /
      │                                 │                │               transient error
      │                          Release Claimed   ┌─────┴──────┐       ScheduleRetry
      │                          (after SHA        │ Completed  │       (continuation)
      │                           recording)       │            │              │
      │                                 │          └─────┬──────┘              │
      │                                 │                │                     │
      │              Terminal state ────┘          Record SHA            Back to Claimed
      │              (Merged/Closed)               in ReviewedSha
      │                    │
      │               Clean workspace
      │               Remove ReviewedSha
      │
      └───── next poll: SHA differs from ReviewedSha ─────────────────────────┘
```

### 4.1 State Definitions

| State | Description |
|-------|-------------|
| **Candidate** | Open, non-draft PR in an active review state. Not yet claimed by the orchestrator. |
| **Claimed** | Orchestrator has selected this PR for dispatch. Prevents double-dispatch. |
| **Running** | Agent session is actively reviewing the PR. |
| **Completed** | Review was submitted. The PR's head SHA is recorded. Not re-dispatched until a new push changes the SHA. |
| **Terminal** | PR was merged, closed, or approved. Workspace is cleaned and `ReviewedSha` entry is removed. |

### 4.2 Re-Review Trigger

A PR transitions from Completed back to Candidate automatically when:

1. A new commit is pushed to the PR branch (head SHA changes).
2. The next poll tick fetches the updated `TrackedWorkItem` with the new `HeadSha`.
3. `ShouldDispatch` compares the new `HeadSha` against `ReviewedSha[prId]` and detects a mismatch.
4. The PR is re-dispatched for a fresh review.

---

## 5. Orchestrator Dispatch Rules

### 5.1 ShouldDispatch Decision Sequence

The dispatch eligibility check for pull requests follows this order:

```
1. if prId in Running       → not eligible (already running)
2. if prId in Claimed       → not eligible (claimed for dispatch)
3. if ReviewedSha[prId] == currentHeadSha
                            → not eligible (already reviewed at this commit)
   else if SHA differs      → remove prId from Completed (stale; new SHA detected)
4. return eligible
```

Step 3 implicitly handles new PRs — a PR with no `ReviewedSha` entry passes through unconditionally. The existing issue-specific blocker check (step 4 in issues) is unaffected.

### 5.2 Unchanged Dispatch Behaviors

The following orchestrator behaviors are not affected and remain as defined in the Symphony SPEC:

- `HasAvailableSlots` concurrency gating.
- `DispatchSorter.Sort` ordering.
- `ReconcileAsync` stall detection and terminal-state cleanup.
- Retry scheduling and backoff.

---

## 6. Agent Review Behavioral Contract

### 6.1 COMMENT-Only Policy

The `SubmitReview` tool always submits reviews with the `COMMENT` event type. The tool now accepts structured payloads (`summaryJson`, `commentsJson`) and no longer exposes the legacy `reviewEvent/body` tool shape.

Automated bot reviews must not affect a PR's approval or rejection status on GitHub. Using `COMMENT` provides feedback without interfering with the team's human code review process or triggering auto-merge rules.

### 6.2 ReviewCompleted Flag

The `ReviewCompleted` flag on the tool provider signals the runner loop that the review is done so it can exit normally. This flag is the sole indicator used by the orchestrator to determine whether a SHA should be recorded.

---

## 7. Post-Review Completion Flow

### 7.1 Successful Review (ReviewSubmitted == true)

When the agent exits normally after calling `SubmitReview`:

1. Record `ReviewedSha[prId] = workItem.HeadSha` (only if `HeadSha` is non-null).
2. Remove `prId` from `Claimed`.

The PR is now in the Completed state. It will not be re-dispatched until its head SHA changes.

### 7.2 Review Not Submitted (ReviewSubmitted == false)

When the agent exits normally but did not call `SubmitReview` (turns exhausted), no SHA is recorded. The orchestrator schedules a continuation retry per Symphony SPEC §8.4.

### 7.3 Failed or Cancelled Runs

Unchanged per Symphony SPEC §7.3: failed runs schedule a retry; cancelled runs release the Claimed slot.

### 7.4 Terminal State Cleanup

On every reconcile tick two cleanup paths run:

1. **Running-entry path** — when `ReconcileAsync` detects that a currently running PR has reached a terminal state, it terminates the agent, cleans the workspace, and removes the `ReviewedSha` entry.

2. **Completed-PR path** — a dedicated pass (`CleanupTerminalReviewedShaAsync`) scans all `ReviewedSha` entries whose IDs are not in `Running` or `Claimed`, batch-fetches their current states, and removes entries for any that are terminal. This handles PRs that completed review earlier and merged or were closed while no agent was running.

Both paths prevent stale SHA entries from accumulating for PRs that are no longer active.

---

## 8. Configuration

### 8.1 Active Fields

| Field | Description |
|-------|-------------|
| `PullRequestWorkflowPath` | Controls whether PR review dispatch is enabled. Clearing this field disables PR tracking entirely. |
| `PullRequestActiveStates` | States considered active for candidate selection. Default: `["Pending Review", "Review Requested", "Changes Requested"]` |
| `PullRequestTerminalStates` | States that trigger terminal cleanup. Default: `["Merged", "Closed", "Approved"]` |
| `MaxConcurrentPullRequestAgents` | Dedicated concurrency limit for PR agents. `0` shares the global limit. |
| `Polling.IntervalMs` | Poll tick frequency in milliseconds. |

### 8.2 No Label Configuration

There is no label-gate configuration. All open, non-draft PRs in active states are automatically eligible. To restrict PR tracking scope, adjust `PullRequestActiveStates`.
