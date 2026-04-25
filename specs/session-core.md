# DotCraft Session Core Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-18 |

Purpose: Define the current **server-managed** session model (Thread / Turn / Item) used by `DotCraft.Core`, including lifecycle, persistence, event semantics, approval semantics, and adapter boundaries.

## 1. Scope

This specification defines the **internal domain model and execution engine** for channels whose conversation state is owned by the server and executed through `ISessionService`.

For the external JSON-RPC API that projects these primitives to out-of-process clients, see the [DotCraft AppServer Protocol Specification](appserver-protocol.md).

| Document | Defines |
|----------|---------|
| `session-core.md` | Domain model, lifecycle rules, event semantics, persistence layout, approval semantics, and adapter contracts inside `DotCraft.Core`. |
| `appserver-protocol.md` | JSON-RPC methods, notifications, transport rules, wire DTOs, error codes, and approval mechanics for out-of-process clients. |

### 1.1 In-Scope Channels

The Session Protocol is the active execution model for:

- CLI
- ACP
- QQ
- WeCom
- GitHubTracker

These channels create and resume server-managed threads whose canonical JSONL history lives under `.craft/threads/active|archived/`, while queryable metadata and agent session blobs live in `.craft/state.db`. They submit turns through Session Core and consume `SessionEvent` streams through thin adapters.

### 1.2 Explicit Exemptions

The following channels are intentionally **not** part of the unified session model:

- **API**: Client-managed history through `MapOpenAIChatCompletions` and framework-managed request/session behavior.
- **AG-UI**: Client-managed history through `MapAGUI` and frontend-owned thread state.

### 1.3 Design Intent

The purpose of the Session Protocol is to unify the **server-managed** channels behind one core model:

- shared Thread / Turn / Item primitives
- shared execution path through `ISessionService`
- shared event semantics for adapters
- shared persistence and resume behavior where server-owned history exists

This boundary is intentional. DotCraft does **not** attempt to force client-owned channels into the same persistence model when that would conflict with their native architecture.

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Unified session primitives**: Define Thread, Turn, and Item as the shared model for all server-managed channels.
2. **Single server-side execution path**: In-scope channels invoke the agent through Session Core rather than channel-specific streaming loops.
3. **Unified event stream**: Session Core emits a structured event stream (item started, delta, completed) that server-managed channel adapters translate into transport-specific output.
4. **Cross-channel resume for compatible server-managed identities**: Threads can be discovered and resumed across channels that share the same identity shape.
5. **Thin adapters**: Channel-specific code should primarily translate transport messages and approvals, not own session orchestration logic.
6. **Approval flow unification**: HITL approval requests are modeled as Items within a Turn, with a defined lifecycle that in-scope adapters implement.

### 2.2 Non-Goals

- **Changing LLM/tool execution internals**: The Microsoft.Extensions.AI pipeline (`FunctionInvokingChatClient`, `TracingChatClient`, etc.) remains as-is. Session Core wraps it; it does not replace it.
- **Prescribing channel-specific UX**: How a QQ bot renders a diff versus how ACP renders it is an adapter concern. The protocol defines *what* happened, not *how* to display it.
- **Real-time cross-device sync**: Session Core does not push notifications to idle channels when a thread updates elsewhere. Channels discover thread state on resume.
- **Multi-user thread collaboration**: Collaborative editing of a thread (multiple users editing simultaneously) is not in scope. Sequential group input is supported as described in Section 17.
- **Unifying client-managed channels**: API and AG-UI retain their own hosting and history models.
- **Standards-body compatibility**: This spec defines DotCraft's internal session model. It does not attempt to be an OpenAI/Codex-compatible public standard.
- **Replacing `IChannelService`**: The channel module contract (`IChannelService`, `IDotCraftModule`) is unchanged. Session Core is a layer *inside* channel implementations, not a replacement of the module system.

## 3. System Overview

### 3.1 Main Components

1. **Session Core** (`DotCraft.Protocol`)
   - Owns Thread/Turn/Item lifecycle and state machines.
   - Wraps the agent execution pipeline (`AgentFactory` + `RunStreamingAsync`).
   - Emits a structured event stream consumed by adapters.
- Persists canonical thread history to JSONL under `.craft/threads/active|archived/` and stores thread metadata plus agent session state in `.craft/state.db`.
   - Enforces per-thread mutual exclusion.

2. **Channel adapters** (per in-scope channel, in module assemblies)
   - Translate between the channel's transport (stdio, HTTP, WebSocket, bot API) and Session Core API calls.
   - Subscribe to Session Core events and render them in channel-specific format.
   - Handle channel-specific concerns: authentication, message formatting, rate limiting.
   - Implement approval routing by translating `ApprovalRequest` Items into channel UX.

3. **Persistence Layer** (`DotCraft.Protocol`)
- Appends thread history to `.craft/threads/{active|archived}/{threadId}.jsonl`.
- Stores agent history in the SQLite `thread_sessions` table inside `.craft/state.db`.
   - Provides thread discovery and session reconstruction on resume.

4. **Event stream** (in-process, `DotCraft.Protocol`)
   - Delivers Session Core events to the active channel adapter.
   - Per-thread event stream: each Thread has one active consumer.
   - Delivery is decoupled from channel rendering.

### 3.2 Abstraction Layers

The server-managed session protocol is organized into five layers, ordered from closest to the user to closest to the model:

1. **Transport Layer** (per channel)
   - The raw communication mechanism: stdio JSON-RPC (ACP), WebSocket (QQ), HTTPS webhook (WeCom), in-process (CLI), internal (GitHubTracker).
   - API and AG-UI have their own HTTP/SSE transports but bypass Session Core.
   - Each channel keeps its existing transport.

2. **Adapter Layer** (per channel)
   - Translates transport messages into Session Core calls: `CreateThread`, `ResumeThread`, `SubmitInput`, `ResolveApproval`.
   - Translates Session Core events into transport messages: text chunks, tool call notifications, approval prompts.
   - This is the only new layer that in-scope channel modules need to implement. It replaces channel-specific session orchestration logic.

3. **Session Core Layer** (`DotCraft.Core`, new)
   - Manages Thread/Turn/Item state machines.
   - Orchestrates a Turn: creates Items, invokes the agent, emits events, handles approval pauses.
   - Calls into the Agent Execution Layer and Persistence Layer.
   - This is the "one harness" shared by all server-managed channels.

4. **Agent Execution Layer** (`DotCraft.Core`, existing)
   - `AgentFactory.CreateAgentWithTools` — tool aggregation, pipeline construction.
   - `agent.RunStreamingAsync` — the Microsoft.Extensions.AI agent loop.
   - `FunctionInvokingChatClient` — tool call orchestration.
   - `TracingChatClient`, `DynamicToolInjectionChatClient` — pipeline middleware.
   - Unchanged by this spec. Session Core consumes its output.

5. **Persistence Layer** (`DotCraft.Core`)
- Thread JSONL storage in `.craft/threads/active|archived/` plus metadata/session storage in `.craft/state.db`
   - SQLite-backed thread discovery
   - Agent session reconstruction on resume

### 3.3 Layer Diagram

```
Server-managed channels

  ACP      CLI      QQ      WeCom      GitHubTracker
   │        │        │         │              │
   └────────┴────────┴─────────┴──────────────┘
                         │
                    Adapter Layer
                         │
                    Session Core
       (Thread lifecycle, Turn orchestration, events)
                         │
                 Agent Execution Layer
                         │
                  Persistence Layer
(`.craft/threads/**/*.jsonl`, `.craft/state.db`)


Client-managed channels (outside Session Core)

  API   -> `MapOpenAIChatCompletions` + tracing middleware
  AG-UI -> `MapAGUI` + tracing middleware
```

### 3.4 Relationship to Existing Code

| Existing Component | Session Protocol Relationship |
|--------------------|-------------------------------|
| `AgentRunner` | Session Core subsumes its responsibilities. `AgentRunner.RunAsync` logic (session load, hook execution, streaming, save, compaction, consolidation) is now implemented on top of Session Core behavior. |
| `AgentFactory` | Unchanged. Session Core calls `CreateAgentWithTools` and `CreateAgent` as before. |
| `SessionStore` | Removed. Its responsibilities are now split between thread/session file persistence and `ISessionService`. |
| `SessionGate` | Becomes an internal implementation detail of Session Core. Channels no longer call `AcquireAsync` directly. |
| `IApprovalService` | Remains the approval interface. Session Core delegates approval requests to the channel adapter, while the request and response are modeled as Items with explicit lifecycle. |
| `IChannelService` | Unchanged. A channel module still implements `IChannelService` for Gateway integration. The adapter is an internal component of the channel's `IChannelService` implementation. |
| `HookRunner` | Session Core invokes hooks (PrePrompt, Stop, PreToolUse, PostToolUse) at the appropriate points in the Turn lifecycle. Channels no longer invoke hooks directly. |
| `TraceCollector` | Session Core records trace events. Channels no longer interact with `TraceCollector` directly. |

### 3.5 External Dependencies

- **Microsoft.Extensions.AI**: `IChatClient`, `AITool`, `FunctionInvokingChatClient` — the agent execution pipeline.
- **Microsoft.Agents.AI**: `AIAgent`, `AgentSession`, `AgentResponseUpdate` — the agent session model.
- **Existing DotCraft.Core**: `AppConfig`, `SkillsLoader`, `MemoryStore`, `ToolProviderCollector` — workspace infrastructure.
- **Channel transports**: Each channel's transport library (NapCat for QQ, ASP.NET for API/AG-UI/WeCom, custom stdio for ACP).

## 4. Core Domain Model

### 4.1 Entities

#### 4.1.1 Thread

A Thread is a persistent conversation between one user and one agent, tied to a workspace.

Fields:

- `Id` (string)
  - Globally unique identifier. Format: `thread_{timestamp}_{random}` (e.g., `thread_20260315_a3f2k9`).
  - Assigned by Session Core on creation. Immutable after creation.
- `WorkspacePath` (string)
  - Absolute path to the workspace this Thread belongs to.
- `UserId` (string, nullable)
  - Opaque user identifier from the originating channel. Used for thread discovery ("show me my threads").
  - Null for system-initiated threads (Cron, Heartbeat).
- `OriginChannel` (string)
  - Name of the channel that created this Thread (e.g., `"qq"`, `"acp"`, `"cli"`).
  - Informational only; does not restrict which channels can resume the Thread.
- `DisplayName` (string, nullable)
  - Human-readable label. Defaults to the first user message text (truncated). Can be set explicitly.
- `Status` (enum: `Active`, `Paused`, `Archived`)
  - See Section 5.1 for lifecycle rules.
- `CreatedAt` (UTC timestamp)
- `LastActiveAt` (UTC timestamp)
  - Updated when a Turn starts or completes.
- `Metadata` (dictionary, string → string)
  - Extensible key-value pairs for channel-specific data (e.g., QQ group ID, ACP workspace URI).
  - Session Core preserves but does not interpret Metadata.
- `Configuration` (ThreadConfiguration, nullable)
  - Per-thread agent configuration (MCP servers, mode, extensions). See Section 16. Null means workspace defaults apply.
- `Turns` (ordered list of Turn)
  - Append-only. Turns are never removed from a Thread.
- `QueuedInputs` (ordered list of QueuedTurnInput)
  - FIFO inputs submitted while a Turn is running. The queue is part of canonical thread state and is persisted in the rollout file.
  - When a running Turn completes successfully, Session Core dequeues at most one queued input and starts it as the next Turn. Failed or cancelled Turns do not automatically consume queued inputs.

#### 4.1.1.1 QueuedTurnInput

A QueuedTurnInput is a durable snapshot of user input waiting to become a future Turn.

Fields:

- `Id` (string)
  - Globally unique queued-input identifier.
- `ThreadId` (string)
  - Parent Thread ID.
- `NativeInputParts` (ordered list)
  - Transport-native input parts, such as text, file references, skill references, command references, or local image references.
- `MaterializedInputParts` (ordered list)
  - Model-visible input parts after materialization. This snapshot is used when the queued input is executed.
- `DisplayText` (string)
  - Human-readable queue label derived from the native snapshot.
- `Sender` (SenderContext, nullable)
  - Optional sender identity for group sessions.
- `Status` (string)
  - `"queued"` for normal FIFO execution or `"guidancePending"` after the user promotes the input into current-Turn guidance.
- `CreatedAt` (UTC timestamp)
- `ReadyAfterTurnId` (string, nullable)
  - Active Turn ID observed when the input was queued.

#### 4.1.2 Turn

A Turn is one unit of agent work initiated by user input. A Turn starts when the user submits a message and ends when the agent finishes responding (or fails, or is cancelled).

Fields:

- `Id` (string)
  - Unique within the Thread. Format: `turn_{sequence}` (e.g., `turn_001`).
- `ThreadId` (string)
  - Reference to the parent Thread.
- `Status` (enum: `Running`, `Completed`, `WaitingApproval`, `Failed`, `Cancelled`)
  - See Section 5.2 for lifecycle rules.
- `Input` (Item)
  - The user's input Item that initiated this Turn. Always of type `UserMessage`.
- `Items` (ordered list of Item)
  - All Items produced during this Turn, including the Input. Append-only.
- `StartedAt` (UTC timestamp)
- `CompletedAt` (UTC timestamp, nullable)
  - Set when Status transitions to a terminal state (`Completed`, `Failed`, `Cancelled`).
- `TokenUsage` (object, nullable)
  - `InputTokens` (long)
  - `OutputTokens` (long)
  - `TotalTokens` (long)
  - Accumulated during the Turn from `UsageContent` in the streaming response.
- `Error` (string, nullable)
  - Human-readable error description when Status is `Failed`.

#### 4.1.3 Item

An Item is the atomic unit of input/output within a Turn. Every piece of information exchanged between the user, agent, and tools is represented as an Item with a typed payload and an explicit lifecycle.

Fields:

- `Id` (string)
  - Unique within the Turn. Format: `item_{sequence}` (e.g., `item_001`).
- `TurnId` (string)
  - Reference to the parent Turn.
- `Type` (enum)
  - `UserMessage` — User's input text.
  - `AgentMessage` — Agent's response text (may be streamed incrementally).
  - `ReasoningContent` — Agent's internal reasoning/thinking (if exposed by the model).
  - `CommandExecution` — Server-observed shell execution stream for `Exec`-style tools. Payload includes command metadata and aggregated output.
  - `ToolCall` — Agent invokes a tool. Payload includes tool name and arguments.
  - `ToolResult` — Result of a tool invocation. Payload includes result text and success/failure.
  - `ApprovalRequest` — Agent requests user approval for a sensitive operation.
  - `ApprovalResponse` — User's approval decision (approved/rejected).
  - `Error` — An error occurred during the Turn.
  - `SystemNotice` — Persistent system-level marker in the conversation timeline (e.g. context compaction point). Emits `item/started` + `item/completed` back-to-back; no streaming phase.
- `Status` (enum: `Started`, `Streaming`, `Completed`)
  - `Started` — Item has been created, payload may be partial or empty.
  - `Streaming` — Item is receiving incremental updates (deltas). Valid for `AgentMessage`, `ReasoningContent`, runtime-projected `CommandExecution`, and AppServer-projected streamed `ToolCall` argument previews.
  - `Completed` — Item is finalized, payload is complete.
- `Payload` (type-specific object)
  - See Section 4.2 for payload schemas per Item type.
- `CreatedAt` (UTC timestamp)
- `CompletedAt` (UTC timestamp, nullable)

#### 4.1.4 SessionIdentity

A SessionIdentity maps a channel-specific user context to a Thread. It is used for thread discovery and creation.

Fields:

- `ChannelName` (string)
  - The channel requesting the operation (e.g., `"qq"`, `"acp"`).
- `UserId` (string, nullable)
  - Channel-specific user identifier.
- `ChannelContext` (string, nullable)
  - Channel-specific context key (e.g., QQ group ID, ACP workspace URI). Allows multiple threads per user within the same channel.
- `WorkspacePath` (string)
  - The workspace this identity operates in.

Thread discovery uses `SessionIdentity` to find existing threads:
- `FindThreads(identity)` → returns threads matching the workspace, user, and optionally channel context.
- The adapter decides whether to resume an existing thread or create a new one.

### 4.2 Item Payload Schemas

Each Item type has a specific payload structure:

#### UserMessage

```
{
  "text": string,          // Compatibility/display text derived from nativeInputParts
  "nativeInputParts": [    // Optional native input snapshot persisted as the source of truth
    InputPart
  ],
  "materializedInputParts": [ // Optional model-visible input snapshot after server-side materialization
    InputPart
  ],
  "senderId": string,      // Individual sender within a group session (nullable, see Section 17.1)
  "senderName": string,    // Display name of the sender (nullable)
  "senderRole": string,    // Sender role when available from channel adapter (nullable)
  "channelName": string,   // Originating channel for this user message (nullable)
  "channelContext": string,// Channel-specific context for this message (nullable)
  "groupId": string,       // Group/chat identifier (nullable)
  "images": [              // Optional local image metadata for UI rehydration
    {
      "path": string,      // Absolute attachment path on host
      "mimeType": string,  // Optional MIME hint
      "fileName": string   // Optional original filename
    }
  ],
  "triggerKind": string,   // Optional automation trigger marker: "heartbeat" | "cron" | "automation"
  "triggerLabel": string,  // Optional human-readable source label (e.g. cron job name, task title)
  "triggerRefId": string   // Optional routing id for click-through (e.g. cron job id, task id)
}
```

`nativeInputParts` is authoritative for history rendering and editor rehydration when present. `materializedInputParts` captures the exact prompt/image snapshot that Session Core received after transport-side input materialization. `text` remains for compatibility and preview generation but is no longer the sole source of truth for user-message reconstruction.

The optional `triggerKind` trio is populated by Session Core when a turn is submitted inside a `TurnTriggerScope` (see `DotCraft.Protocol.TurnTriggerScope`). The automation-side runners set the scope so that heartbeat / cron (`AgentRunner`) and Automations (`AutomationSessionClient.SubmitTurnAsync`) synthesized messages carry a stable marker that clients can use to render an "automation-sourced" affordance and route click-through to the originating job/task. Fields are absent when the turn originates from a real user input.

#### AgentMessage

```
{
  "text": string          // Accumulated response text (final value after streaming)
}
```

Delta payload (during streaming):

```
{
  "textDelta": string     // Incremental text chunk
}
```

#### ReasoningContent

```
{
  "text": string          // The reasoning/thinking text
}
```

#### ToolCall

```
{
  "toolName": string,     // Name of the tool being called
  "arguments": object,    // Tool arguments as key-value pairs
  "callId": string        // Correlation ID linking ToolCall to ToolResult
}
```

#### ToolResult

```
{
  "callId": string,       // Matches the ToolCall.callId
  "result": string,       // Textual result
  "success": boolean      // Whether the tool execution succeeded
}
```

#### CommandExecution

```
{
  "callId": string,               // Correlates to the underlying Exec-style tool call
  "command": string,              // Shell command text
  "workingDirectory": string,     // Effective working directory
  "source": string,               // "host" or "sandbox"
  "status": string,               // "inProgress", "completed", "failed", "cancelled", "backgrounded", "killed", or "lost"
  "aggregatedOutput": string,     // Full accumulated output shown to the user
  "sessionId": string | null,     // Background terminal id when the command continues after tool return
  "outputPath": string | null,    // Host-local output log for background terminal sessions
  "originalOutputChars": number | null,
  "truncated": boolean | null,
  "backgroundReason": string | null,
  "exitCode": number | null,      // Process exit code when available
  "durationMs": number | null     // Total wall-clock duration when available
}
```

When an `Exec`-style tool returns while its process is still alive, the
`CommandExecution` Item is completed with `status = "backgrounded"`. Later
process lifecycle changes are represented by the background terminal runtime,
not by appending deltas to an already completed Turn.

#### ApprovalRequest

```
{
  "approvalType": string, // "file" or "shell"
  "operation": string,    // For file: "read", "write", "edit", "list". For shell: the command.
  "target": string,       // For file: the path. For shell: the working directory.
  "requestId": string     // Unique ID for correlating with ApprovalResponse
}
```

#### ApprovalResponse

```
{
  "requestId": string,    // Matches the ApprovalRequest.requestId
  "approved": boolean     // User's decision
}
```

#### Error

```
{
  "message": string,      // Human-readable error description
  "code": string,         // Machine-readable error code (e.g., "agent_error", "timeout")
  "fatal": boolean        // Whether the error terminates the Turn
}
```

#### SystemNotice

```
{
  "kind": string,              // Notice classifier. Currently "compacted"; future kinds are additive.
  "trigger": string,           // For kind="compacted": "auto" | "reactive" | "manual"
  "mode": string,              // For kind="compacted": "micro" | "partial"
  "tokensBefore": number,      // Approximate input tokens right before compaction ran
  "tokensAfter": number,       // Approximate input tokens after compaction ran
  "percentLeftAfter": number,  // Fraction of EffectiveContextWindow still available (0.0 - 1.0)
  "clearedToolResults": number // Count of tool results cleared by the micro-compact pass (0 for partial only)
}
```

`SystemNotice` items are created by Session Core and persisted via the normal
rollout/`turn.Items` pipeline, so they survive thread reload and round-trip
through `thread/read`. Clients treat them as inline dividers in the timeline
rather than part of the model conversation.

### 4.3 Stable Identifiers and Normalization Rules

- **Thread ID**: Generated by Session Core. Format `thread_{yyyyMMdd}_{6-char-random}`. Must be unique within the workspace. Used as the primary key for persistence and cross-channel resume.
- **Turn ID**: Sequential within a Thread. Format `turn_{3-digit-sequence}`. Assigned by Session Core when a Turn starts.
- **Item ID**: Sequential within a Turn. Format `item_{3-digit-sequence}`. Assigned by Session Core when an Item is created.
- **UserId Normalization**: Session Core stores `UserId` as-is from the adapter. Cross-channel user identity resolution (is QQ user X the same as ACP user Y?) is out of scope for this spec.

## 5. Session Lifecycle Specification

### 5.1 Thread Lifecycle

```
                    ┌──────────┐
     CreateThread   │          │
    ─────────────►  │  Active  │ ◄──── ResumeThread
                    │          │
                    └────┬─────┘
                         │
              ┌──────────┼──────────┐
              │                     │
              ▼                     ▼
        ┌──────────┐         ┌───────────┐
        │  Paused  │         │ Archived  │
        └────┬─────┘         └───────────┘
             │
             │ ResumeThread
             ▼
        ┌──────────┐
        │  Active  │
        └──────────┘
```

**Transitions**:

- `CreateThread(identity)` → `Active`
  - Session Core generates a Thread ID, sets `CreatedAt` and `LastActiveAt` to now.
  - The adapter provides `SessionIdentity` with channel name, user ID, and context.

- `Active` → `Paused`
  - Triggered by explicit adapter request or by inactivity timeout (configurable, default: none).
  - A Paused thread can be resumed by any compatible server-managed channel.
  - No Turn may be started on a Paused thread without first resuming it.

- `Paused` → `Active` (via `ResumeThread`)
  - Any compatible in-scope adapter can resume a Paused thread by calling `ResumeThread(threadId)`.
  - Session Core loads the thread state from persistence, reconstructs the agent session, and sets status to Active.
  - `LastActiveAt` is updated.

- `Active` → `Archived`
  - Triggered by explicit adapter request or by archival policy (e.g., "archive threads inactive for 30 days").
  - Archived threads are read-only. They can be listed and inspected but not resumed.
  - Archival is permanent within this spec version (no unarchive).

**Invariants**:

- At most one Turn may be `Running` or `WaitingApproval` on a Thread at any time.
- A Thread may have Turns from different channels (cross-channel resume). Each Turn records which channel originated it.

### 5.2 Turn Lifecycle

```
                    ┌───────────┐
    SubmitInput     │           │
   ─────────────►   │  Running  │ ◄──── ApprovalResolved
                    │           │
                    └─────┬─────┘
                          │
           ┌──────────────┼─────────────────┐
           │              │                 │
           ▼              ▼                 ▼
   ┌──────────────┐ ┌──────────┐    ┌────────────┐
   │WaitingApproval│ │Completed │    │  Failed    │
   └──────────────┘ └──────────┘    └────────────┘
                                    ┌────────────┐
                                    │ Cancelled  │
                                    └────────────┘
```

**Transitions**:

- `SubmitInput(threadId, content)` → `Running`
  - Session Core creates a new Turn, creates a `UserMessage` Item from the input, invokes the agent.
  - Precondition: Thread status is `Active` and no other Turn is `Running` or `WaitingApproval`.

- `Running` → `WaitingApproval`
  - The agent's tool execution encounters a sensitive operation requiring user approval.
  - Session Core creates an `ApprovalRequest` Item and pauses agent execution.
  - The adapter presents the approval request to the user.

- `WaitingApproval` → `Running`
  - The adapter calls `ResolveApproval(turnId, requestId, approved)`.
  - Session Core creates an `ApprovalResponse` Item and resumes agent execution.

- `Running` → `Completed`
  - The agent finishes its response. The final `AgentMessage` Item is marked Completed.
  - Session Core runs post-turn operations: save session, run Stop hooks, compaction, consolidation.

- `Running` → `Failed`
  - An unrecoverable error occurs: agent exception, tool execution error, timeout.
  - Session Core creates an `Error` Item, sets `Turn.Error`, and runs cleanup.

- `Running` or `WaitingApproval` → `Cancelled`
- The adapter requests cancellation (e.g., user sends `/cancel`, channel disconnects).
- Session Core cancels the agent execution via `CancellationToken`, completes any currently streaming agent/reasoning Items with their accumulated text, saves partial state, and rebuilds or invalidates the persisted `AgentSession` so the next turn includes the cancelled turn's user input and completed partial assistant output.

**Terminal states**: `Completed`, `Failed`, `Cancelled`. A Turn in a terminal state cannot transition.

### 5.3 Item Lifecycle

```
    ┌─────────┐      ┌───────────┐      ┌───────────┐
    │ Started │ ───► │ Streaming │ ───► │ Completed │
    └─────────┘      └───────────┘      └───────────┘
         │                                     ▲
         └─────────────────────────────────────┘
              (non-streaming items skip Streaming)
```

**Transitions**:

- `Started` → `Streaming` (optional, for `AgentMessage`, `ReasoningContent`, and runtime-projected `CommandExecution`)
  - Session Core begins receiving incremental content from the agent.
  - Each delta emits an `item/delta` event with the incremental payload.
  - For streamed tool arguments, hosts such as AppServer may project these deltas as tool-call argument preview notifications on the wire while the canonical persisted `ToolCall` payload is finalized at completion.

- `Streaming` → `Completed`
  - The agent finishes producing content for this Item.
  - The Item's payload contains the final accumulated value.

- `Started` → `Completed` (for non-streaming items)
  - Items like `ToolResult`, `ApprovalRequest`, `ApprovalResponse`, `Error` are created with their full payload and immediately completed.
  - `ToolCall` is usually completed directly, but hosts may expose an intermediate streaming preview of argument construction before the final completed payload is persisted.

**Invariants**:

- An Item's Status never moves backward.
- A Completed Item's payload is immutable.
- Items within a Turn are ordered by creation time. This order is the canonical sequence of events within the Turn.

### 5.4 Turn Item Sequence (Normative)

A typical Turn produces Items in this order:

```
1. UserMessage (input)
2. [ReasoningContent] (if model exposes thinking)
3. [ToolCall → ToolResult]* (zero or more tool invocations)
   3a. [ApprovalRequest → ApprovalResponse] (within a tool call, if approval needed)
4. AgentMessage (final response, streamed)
5. [Error] (if something went wrong)
```

The sequence may recurse: the agent may call tools, receive results, reason again, call more tools, and then respond. Session Core emits Items in the order they occur. The adapter renders them according to its channel's capabilities.

### 5.5 Cross-Channel Resume Semantics

When a channel adapter resumes a Thread that was created by a different channel:

1. The adapter calls `ResumeThread(threadId)`.
2. Session Core loads the Thread from persistence.
3. Session Core reconstructs the `AgentSession` (conversation history) by replaying the stored Items into the Microsoft.Agents.AI session format.
4. The Thread's `Status` is set to `Active`, `LastActiveAt` is updated.
5. The adapter can now call `SubmitInput` to start a new Turn.
6. The new Turn's Items are attributed to the resuming channel (recorded in Turn metadata).

The resumed agent has full context of previous Turns regardless of which channel originated them.

## 6. Event Model

### 6.1 Overview

Session Core emits a structured event stream during Turn execution. The event stream is the **contract between Session Core and channel adapters**: adapters consume events and translate them to their transport format. This replaces channel-specific `agent.RunStreamingAsync` consumption loops.

Events are delivered in-process via a callback or async enumerable pattern. There is no network transport for events — adapters run in the same process as Session Core.

### 6.2 Event Envelope

Every event carries a common envelope:

```
SessionEvent
├── EventId: string           // Unique event ID, monotonically increasing within a Turn
├── EventType: string         // One of the types defined in Section 6.3
├── ThreadId: string          // Parent Thread
├── TurnId: string            // Parent Turn (null for thread-level events)
├── ItemId: string            // Related Item (null for turn/thread-level events)
├── Timestamp: UTC timestamp  // When the event was emitted
└── Payload: object           // Event-type-specific data
```

### 6.3 Event Types

#### Thread Events

- **`thread/created`**
  - Emitted when a new Thread is created.
  - Payload: `{ thread: Thread }` (full Thread object with initial state).

- **`thread/resumed`**
  - Emitted when a Paused or previously inactive Thread is resumed.
  - Payload: `{ thread: Thread, resumedBy: string }` (channel name that resumed it).

- **`thread/statusChanged`**
  - Emitted when Thread status changes (Active → Paused, Active → Archived).
  - Payload: `{ previousStatus: string, newStatus: string }`.

- **`thread/renamed` (Wire Protocol only; not a `SessionEvent`)**
  - Display name changes are applied in Session Core via `ISessionService.RenameThreadAsync` or when the first user message on a turn sets `Thread.DisplayName` (see turn input handling and `Thread.DisplayName` in this specification). Session Core does **not** enqueue a `SessionEvent` on the turn/event stream for rename-only updates (there is no separate thread-level event type consumed by in-process adapters the same way as `thread/created`).
  - Hosts that multiplex **multiple Wire clients** onto the same Session Core process (e.g. DotCraft AppServer) **SHOULD** broadcast a `thread/renamed` notification on the AppServer Wire Protocol after the display name is updated, including when the change originates from another channel or from automatic titling, so clients such as DotCraft Desktop can refresh thread titles **without** relying on `turn/completed` (which may not be delivered to connections that did not subscribe to that thread). See [AppServer Protocol §4.11 `thread/rename`](appserver-protocol.md#411-threadrename) and [§6.1 `thread/renamed`](appserver-protocol.md#61-thread-notifications).

- **`thread/deleted` (Wire Protocol only; not a `SessionEvent`)**
  - Permanent removal is performed via `ISessionService.DeleteThreadPermanentlyAsync(threadId)`. Session Core removes in-memory state, persisted thread/session data, all tracing sessions/events bound to that thread, and dashboard usage rows associated with the thread or its bound trace sessions; it does **not** enqueue a `SessionEvent` on the turn/event stream (there is no active turn for deletion).
  - Hosts that multiplex **multiple Wire clients** onto the same Session Core process (e.g. DotCraft AppServer) **SHOULD** broadcast a `thread/deleted` notification on the AppServer Wire Protocol after deletion completes, including when deletion is initiated outside Wire (e.g. DashBoard HTTP `DELETE` on `/dashboard/api/sessions/{sessionKey}`), so UIs stay consistent. See [AppServer Protocol §4.9 `thread/delete`](appserver-protocol.md#49-threaddelete) and [§6.1 Thread Notifications](appserver-protocol.md#61-thread-notifications).

#### Turn Events

- **`turn/started`**
  - Emitted when a new Turn begins.
  - Payload: `{ turn: Turn }` (Turn object with `Status = Running`, Input Item included).

- **`turn/completed`**
  - Emitted when a Turn finishes successfully.
  - Payload: `{ turn: Turn }` (final Turn state with all Items, TokenUsage).

- **`turn/failed`**
  - Emitted when a Turn fails.
  - Payload: `{ turn: Turn, error: string }`.

- **`turn/cancelled`**
  - Emitted when a Turn is cancelled.
  - Payload: `{ turn: Turn, reason: string }`.

#### Item Events

- **`item/started`**
  - Emitted when an Item is created.
  - Payload: `{ item: Item }` (Item with `Status = Started`, payload may be partial).
  - Adapters should begin rendering immediately (e.g., show a "typing" indicator for AgentMessage, show tool name for ToolCall).

- **`item/delta`**
  - Emitted for incremental content updates on streaming Items (`AgentMessage`, `ReasoningContent`, `CommandExecution`, and streamed `ToolCall` argument previews).
  - Payload: the delta-specific payload (e.g., `{ textDelta: "chunk of text" }`).
  - May be emitted many times per Item. Adapters that support streaming should forward these to the user progressively.
  - Adapters that do not support streaming (e.g., GitHubTracker) may ignore deltas and wait for `item/completed`.
  - Persistence still uses the final completed Item payload as source of truth; intermediate `ToolCall` argument preview deltas are for progressive rendering.

- **`item/completed`**
  - Emitted when an Item is finalized.
  - Payload: `{ item: Item }` (Item with `Status = Completed`, full payload).

#### Approval Events

- **`approval/requested`**
  - Emitted when the agent requires user approval. Equivalent to `item/started` for an `ApprovalRequest` Item, but distinguished as a separate event type because it requires adapter action (the adapter must present the request and return a response).
  - Payload: `{ item: Item }` (the `ApprovalRequest` Item).
  - The Turn enters `WaitingApproval` status.

- **`approval/resolved`**
  - Emitted when the user resolves an approval request.
  - Payload: `{ item: Item }` (the `ApprovalResponse` Item).
  - The Turn returns to `Running` status.

#### SubAgent Progress Events

- **`subagent/progress`**
  - Emitted periodically (~200ms) during Turn execution when one or more SubAgent tool calls (`SpawnSubagent`) are active.
  - Provides a snapshot of all active SubAgents' real-time execution progress, including the tool currently being executed, cumulative token consumption, and completion status.
  - Payload:

    ```
    {
      "entries": [
        {
          "label": string,          // SubAgent identifier/label (matches the label argument passed to SpawnSubagent)
          "currentTool": string,    // Name of the tool the SubAgent is currently executing (nullable, null when thinking)
          "inputTokens": long,      // Cumulative input token consumption
          "outputTokens": long,     // Cumulative output token consumption
          "isCompleted": boolean    // Whether the SubAgent has finished execution
        }
      ]
    }
    ```

  - **Emission rules**:
    - The event is emitted by a periodic aggregator (~200ms interval) that snapshots the in-process `SubAgentProgressBridge` state.
    - The aggregator starts when the first `SpawnSubagent` tool call begins within a Turn, and stops when the Turn ends or all tracked SubAgents have completed.
    - Each notification contains the **complete snapshot** of all tracked SubAgents (not incremental deltas), so clients can replace their local state on each receipt.
    - The event is injected into the Turn's event stream as a sideband signal — it may interleave with `item/started`, `item/delta`, and `item/completed` events. This is expected behavior.
  - **Relationship to Item events**: SubAgent execution is triggered by `SpawnSubagent` tool calls, which appear as `item/started` (type `toolCall`, toolName `SpawnSubagent`) and `item/completed` (type `toolResult`) events. The `subagent/progress` event provides fine-grained intermediate progress that is not captured by the standard Item lifecycle.
  - **Adapters**: Adapters that render SubAgent progress (e.g., CLI Live Table) should consume `subagent/progress` events to update their UI. Adapters that do not need SubAgent progress may ignore this event type or opt out via `optOutNotificationMethods`.

#### System Events

- **`system/event`**
  - Emitted by Session Core when a system-level maintenance operation occurs during a Turn's post-processing phase. These operations are not part of the agent's conversational output but affect the session's internal state.
  - Payload:

    ```
    {
      "kind": string,          // One of: "compactWarning", "compactError",
                                //         "compacting", "compacted", "compactSkipped", "compactFailed",
                                //         "consolidating", "consolidated"
      "message": string,       // Human-readable description (nullable)
      "percentLeft": double,   // Fraction of the effective context window still unused (nullable; 0.0-1.0)
      "tokenCount": long       // Current estimated prompt token usage (nullable)
    }
    ```

  - **Defined `kind` values**:

    | Kind | Meaning | Timing |
    |------|---------|--------|
    | `compactWarning` | Token usage crossed `WarningThreshold` but not yet `ErrorThreshold`. Advisory only, no compaction is attempted. | Synchronous, post-turn (Step 5k), when threshold is above warning but below auto. |
    | `compactError` | Token usage crossed `ErrorThreshold`. Strong advisory; auto-compaction may trigger on the next turn. | Synchronous, post-turn (Step 5k), when threshold is above error but below auto. |
    | `compacting` | Auto-compaction is starting. `percentLeft`/`tokenCount` reflect the pre-compaction state. | Synchronous, before the `CompactionPipeline` runs. |
    | `compacted` | Compaction finished successfully. Token tracker has been reset and `percentLeft`/`tokenCount` reflect the post-compaction state. | Synchronous, immediately after the pipeline returns `Micro` or `Partial`. |
    | `compactSkipped` | Compaction was evaluated but not executed (e.g. below threshold, nothing new to summarize, circuit breaker tripped). | Synchronous, immediately after the pipeline returns `Skipped`. |
    | `compactFailed` | Compaction attempted but failed (LLM error, cancellation). The circuit breaker may trip after several consecutive failures. | Synchronous, immediately after the pipeline returns `Failed`. |
    | `consolidating` | Memory consolidation is starting. Now driven by the compaction pipeline: the prefix it just summarized is handed to the `MemoryConsolidator`. | Fire-and-forget, immediately after a successful compaction (no post-turn block). |
    | `consolidated` | Memory consolidation completed successfully. MEMORY.md and HISTORY.md have been updated. | After background consolidation completes. |

  - **Emission rules**:
    - System events are emitted during the Turn's post-processing phase (after agent execution completes, before `turn/completed`), except when raised reactively (see below).
    - The threshold advisory events (`compactWarning`, `compactError`) carry `percentLeft` and `tokenCount` so UIs can render a "context almost full" warning bar without needing a separate usage request.
    - Auto-compaction events (`compacting`, `compacted`, `compactSkipped`, `compactFailed`) are synchronous within Step 5k and always fire in the order `compacting` → one terminal event (`compacted` / `compactSkipped` / `compactFailed`).
    - The pipeline may also be invoked **reactively** from the Turn's error path when the model rejects a request with `prompt_too_long` / `context_length_exceeded`. In that case the Turn still fails, but `compacting` followed by `compacted` / `compactFailed` is emitted first so UIs know the history was repaired before the user retries.
    - Consolidation events (`consolidating`, `consolidated`) bracket the fire-and-forget background task spawned by the pipeline after a successful compaction. The Turn's completion is **not** deferred for consolidation.
    - All system events are emitted through the turn-scoped `SessionEventChannel`, so they are guaranteed to arrive before `turn/completed`.
    - The `message` field carries a localized human-readable description suitable for display (on `compactSkipped` / `compactFailed` it contains the machine-readable failure reason, e.g. `circuit_breaker_tripped`, `summary_unavailable`).
  - **Adapters**: Adapters that display session maintenance status (e.g., CLI spinner for consolidation, status text for compaction) should consume `system/event` notifications. Adapters that do not need maintenance status may ignore this event type or opt out via `optOutNotificationMethods`.

#### Usage Events

- **`usage/delta`**
  - Emitted each time the agent completes an LLM iteration and a `UsageContent` is received from the streaming response. Carries the **incremental** token consumption for that single iteration.
  - Payload:

    ```
    {
      "inputTokens": long,      // Input tokens consumed in this iteration
      "outputTokens": long      // Output tokens consumed in this iteration
    }
    ```

  - **Emission rules**:
    - The event is emitted by Session Core immediately after processing a `UsageContent` from the agent's streaming output, provided the token counts are non-zero.
    - Each emission carries only the delta for the current iteration, not cumulative totals. Clients must accumulate deltas locally to display running totals.
    - At most one `usage/delta` event is emitted per LLM iteration (the `UsageContent` is emitted once at the end of each iteration by the provider, not per token).
    - The event is a sideband signal — it may interleave with `item/started`, `item/delta`, and `item/completed` events. This is expected behavior.
  - **Relationship to Turn.TokenUsage**: The sum of all `usage/delta` events for a Turn's main agent equals the main-agent portion of `Turn.TokenUsage`. SubAgent tokens are reported separately via `subagent/progress` and are added to `Turn.TokenUsage` at turn completion.
  - **Adapters**: Adapters that display real-time token consumption (e.g., CLI Thinking/Tool spinners) should consume `usage/delta` events to maintain a running total. Adapters that only need final totals may ignore this event type or opt out via `optOutNotificationMethods`.

### 6.4 Event Delivery Semantics

- **Ordering**: Events within a Turn are emitted in causal order. `item/started` always precedes `item/delta` and `item/completed` for the same Item. `turn/started` always precedes all item events for that Turn. `turn/completed` (or `turn/failed`, `turn/cancelled`) is always the last event for a Turn.

- **At-most-once delivery**: Events are not durably queued. If the adapter is not listening (e.g., channel disconnected mid-turn), events are lost. The adapter can reconstruct state from the persisted Thread on reconnection.

- **Decoupled emission**: Session Core writes events into a per-turn in-memory channel and does not synchronously wait for channel rendering. The event stream is authoritative for the active turn, but it is not a durable queue.

- **Single consumer per Turn**: At most one adapter is actively consuming events for a Turn. This is enforced by the Thread invariant (one Running Turn at a time, started by one adapter).

### 6.5 Event Subscription API

Session Core does not expose a standalone `SubscribeToTurn` API. Instead, the turn-scoped event stream is returned directly from `SubmitInputAsync(...)`:

```
IAsyncEnumerable<SessionEvent> SubmitInputAsync(
    string threadId,
    IList<AIContent> content,
    SenderContext? sender = null,
    ...)
```

The `content` parameter accepts multimodal input (text, images, etc.) as a list of `AIContent` parts. When the transport provides native input metadata (for example native command, skill, or file-reference parts), Session Core persists both the transport-native snapshot and the materialized `AIContent` snapshot on `UserMessagePayload`, derives `UserMessagePayload.Text` from the native snapshot for compatibility/display, and passes the full multimodal materialized content to the agent via `ChatMessage`. A convenience extension method `SubmitInputAsync(string threadId, string text, ...)` wraps plain text into `[new TextContent(text)]` for text-only callers.

`UserMessagePayload.DeliveryMode` is optional and indicates how the user message entered the conversation: `"normal"` (or omitted) for a direct Turn start, `"queued"` for a queued input that later became a Turn, and `"guidance"` for a user request appended to an active Turn.

```
Task<QueuedTurnInput> EnqueueTurnInputAsync(
    string threadId,
    IList<AIContent> content,
    SenderContext? sender = null,
    CancellationToken ct = default,
    SessionInputSnapshot? inputSnapshot = null)
```

Enqueues user input while another Turn may be running. The queue is persisted as append-only rollout records. On successful Turn completion, Session Core automatically dequeues the first input and invokes `SubmitInputAsync` with `DeliveryMode = "queued"`.

```
Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
    string threadId,
    string queuedInputId,
    CancellationToken ct = default)
```

Removes a queued input without starting a Turn.

```
Task<TurnSteerResult> SteerTurnAsync(
    string threadId,
    string expectedTurnId,
    string queuedInputId,
    CancellationToken ct = default,
    SenderContext? sender = null)
```

Marks the referenced queued input as `guidancePending` after validating that `expectedTurnId` still matches the current active Turn. The active execution loop drains pending guidance only at safe model/tool boundaries, appends a `UserMessage` item with `DeliveryMode = "guidance"` at insertion time, removes the queued input, and injects the input into the current model history. If the Turn ends before insertion, pending guidance is restored to `queued`.

The adapter starts a turn and immediately consumes the returned async stream. Callback-style consumption is a helper-layer concern (for example, wrapping the stream in a local event handler), not part of the `ISessionService` contract.

## 7. Channel Adapter Contract

### 7.1 Role

A channel adapter is the boundary between a transport and Session Core. It is responsible for:

- turning inbound user actions into `ISessionService` calls
- turning `SessionEvent` output into channel-specific UX
- routing approval decisions back into the active turn
- exposing thread discovery and resume in whatever UX the channel supports

The adapter is not a new public framework interface. It is an internal part of a channel's existing `IChannelService`.

### 7.2 Contract

The normative contract is intentionally small:

- `CreateThread` / `ResumeThread` / `FindThreads` define thread lifecycle at the transport boundary.
- `GetThread` returns persisted thread state and may load the thread into the in-process cache **without** rebuilding execution resources (e.g. per-thread MCP connections).
- `EnsureThreadLoaded` (or an equivalent internal step before turn execution) loads the thread like `GetThread` and, when `Thread.Configuration` is non-null, ensures the effective agent for that thread matches the persisted configuration. It does **not** change thread status or emit `thread/resumed`. Session Core uses this on turn execution paths when the thread may exist only on disk or was cached without agent hydration (e.g. after host restart).
- `SubmitInput` starts a turn and returns the authoritative event stream for that turn. Before running the agent, Session Core must ensure per-thread configuration (mode, MCP, etc.) has been applied when `Configuration` is present—same outcome as loading via `ResumeThread` from disk.
- `ResolveApproval` and `CancelTurn` let the adapter participate in interactive control flow.
- `SetThreadMode` and `UpdateThreadConfiguration` support per-thread behavior where a channel exposes it.

### 7.3 Design Constraints

- Adapters may choose different UX patterns (streaming text, buffered messages, structured UI, non-interactive execution).
- Adapters must not own persistence, agent lifecycle, or hook orchestration.
- API and AG-UI are not Session Protocol adapters because their history is client-managed.

## 8. Agent Execution Integration

### 8.1 Principle

Session Core wraps the existing agent pipeline rather than redefining it. Agent creation, tool invocation, tracing, and middleware remain existing responsibilities; Session Core standardizes how their output is represented as Threads, Turns, Items, and `SessionEvent`s.

### 8.2 Normative Behavior

For each submitted turn, Session Core must:

- validate thread state and mutual exclusion
- create the Turn and its initial user input item
- execute the agent against the persisted server-managed session
- translate agent output into typed items and events
- collect token usage and other turn-level metadata
- persist updated thread/session state
- emit terminal completion or failure events

### 8.3 Compatibility Boundary

- Session Core owns orchestration.
- Adapters own presentation.
- `AgentRunner` may remain as a compatibility entry point, but it is no longer a separate session model.

## 9. Persistence Specification

### 9.1 Storage Layout

Thread data is stored under the workspace's `.craft/` directory:

```
.craft/
├── threads/
│   ├── active/
│   │   ├── {threadId}.jsonl     # Canonical rollout history for active threads
│   │   └── ...
│   ├── archived/
│   │   ├── {threadId}.jsonl     # Canonical rollout history for archived threads
│   │   └── ...
├── state.db                     # SQLite metadata, agent sessions, tracing, token usage
```

### 9.2 Thread File Format

Each thread is stored as an append-only JSONL rollout. Every line is a `ThreadRolloutRecord` describing one state transition:

```json
{ "kind": "thread_opened", "timestamp": "2026-03-15T10:00:00Z", "threadOpened": { ... } }
{ "kind": "turn_started", "timestamp": "2026-03-15T10:00:01Z", "turnStarted": { ... } }
{ "kind": "item_appended", "timestamp": "2026-03-15T10:00:01Z", "itemAppended": { ... } }
{ "kind": "turn_completed", "timestamp": "2026-03-15T10:02:30Z", "turnCompleted": { ... } }
{ "kind": "queued_input_added", "timestamp": "2026-03-15T10:02:31Z", "queuedInputAdded": { ... } }
{ "kind": "queued_input_removed", "timestamp": "2026-03-15T10:02:32Z", "queuedInputRemoved": { ... } }
```

Session Core reconstructs a `SessionThread` by replaying the rollout file in order.

### 9.3 Agent Session Storage

Serialized `AgentSession` state is stored in the SQLite `thread_sessions` table inside `.craft/state.db`.

Session Core manages the mapping:
- **Save**: After each Turn completes, serialize the `AgentSession` and upsert `thread_sessions.session_json`.
- **Load**: On Thread resume, deserialize `thread_sessions.session_json` via `agent.DeserializeSessionAsync`.

The separation between rollout history and agent session state is intentional:
- The rollout JSONL files are the source of truth for the Session Protocol UI/domain model.
- The `thread_sessions` table is the source of truth for optimized LLM conversation history.

### 9.4 Thread Discovery

Thread discovery is implemented by querying the SQLite `threads` metadata table in `.craft/state.db`. Rollout files remain the canonical conversation history, while `ThreadSummary` rows are derived metadata used by `FindThreadsAsync`.

This database-backed approach avoids replaying every rollout file during discovery while keeping rollout files as the canonical history.

`ThreadSummary` fields returned for each discovered thread:
- `Id`, `Status`, `OriginChannel`, `ChannelContext`
- `UserId`, `WorkspacePath`, `DisplayName`
- `CreatedAt`, `LastActiveAt`
- `TurnCount`

### 9.5 Cross-Channel Resume Protocol

#### Default discovery (no `crossChannelOrigins`)

`FindThreadsAsync(identity, includeArchived, crossChannelOrigins: null)` matches threads by three fields. `ChannelName` on the identity is **not** used as a filter:

| Field | Behavior |
|---|---|
| `WorkspacePath` | Required exact match (case-insensitive) |
| `UserId` | Matched if non-null in identity; null identity field skips this filter |
| `ChannelContext` | `null` identity matches only threads with `ChannelContext = null`; non-null matches exactly |

This means cross-channel discovery is **natural for channels that share the same identity shape**:

- **CLI and ACP** both use `UserId = "local"` and `ChannelContext = null`. They discover each other's threads automatically. A thread created in CLI appears in ACP's session list, and vice versa. This is by design — both are local, single-user channels on the same machine.
- **QQ and WeCom** use per-user, per-context identifiers (`ChannelContext = "group:{id}"`, `"chat:{chatId}"`). Each conversation context has its own isolated thread pool. CLI and ACP cannot see QQ/WeCom threads and vice versa. This is also by design — social channel threads are scoped to their originating context.

#### Opt-in cross-context discovery (`crossChannelOrigins`)

`FindThreadsAsync` accepts an optional fourth parameter: `crossChannelOrigins` (`IReadOnlyList<string>?`, default `null`).

- When `crossChannelOrigins` is **null** or **empty**, behavior is identical to the default discovery above (no extra threads).
- When **non-empty**, the result set is the union of:
  1. Threads that satisfy the default identity predicate (`WorkspacePath` + `UserId` + `ChannelContext` as in the table above), **and**
  2. Threads that match `WorkspacePath` (case-insensitive) + `OriginChannel` contained in `crossChannelOrigins` (case-insensitive string match), **ignoring `ChannelContext`**. This branch does **not** require `UserId` to match the request identity, so channels that use per-job or per-session synthetic user IDs (e.g. `cron:{jobId}`) still appear when the user opts in to that origin.

The union is deduplicated by thread ID and ordered by `LastActiveAt` descending.

This opt-in path exists so clients such as **DotCraft Desktop** (which uses a non-null `ChannelContext` such as `workspace:{path}`) can still list threads created by channels with a different context (e.g. CLI with `ChannelContext = null`) when the user explicitly allows those origin channels.

#### Resume flow

1. **Discovery**: Adapter calls `FindThreadsAsync(identity, includeArchived, crossChannelOrigins)`. Returns threads matching the combined predicate when `crossChannelOrigins` is set, otherwise the default predicate only.
2. **Selection**: The adapter presents the list to the user, or auto-selects the most recently active thread.
3. **Resume**: Adapter calls `ResumeThreadAsync(threadId)`. Session Core sets status to `Active` and updates `LastActiveAt`.
4. **Session Load**: Session Core loads `thread_sessions.session_json`, reconstructing the LLM context.
5. **Ready**: Adapter calls `SubmitInputAsync` to start a new Turn. The Turn's `OriginChannel` records the resuming channel's name.

### 9.6 Legacy Compatibility Policy

Session Core does not implement compatibility paths for older snapshot layouts such as `.craft/sessions/{key}.json`, `.craft/threads/{threadId}.json`, or `.craft/threads/{threadId}.session.json`.

The supported persistence contract is:

- Canonical thread history in `.craft/threads/active|archived/*.jsonl`
- Queryable metadata and serialized agent sessions in `.craft/state.db`

Dashboard trace-session deletion follows the same persistence contract. Deleting one trace session removes that session's trace rows and associated dashboard usage rows; if the session is bound to a thread, deletion cascades through permanent thread deletion. Clearing all trace sessions deletes the selected trace/thread state and associated usage rows, but preserves global usage rows that have no `thread_id` or `session_key`. Bulk trace clearing may run SQLite maintenance (`wal_checkpoint(TRUNCATE)` and conditional `VACUUM`) after deletion to reclaim WAL/free-page space.

### 9.7 Persistence Failure Handling

- **Save failure**: If Session Core cannot write to disk after a Turn completes, the Turn's result is still delivered to the adapter (events were already emitted). The error is logged. The in-memory Thread state is preserved. The next save attempt retries.
- **Load failure**: If Session Core cannot read a Thread file on resume, it returns an error to the adapter. The adapter should inform the user and offer to create a new Thread.
- **Discovery failure**: If a thread file is unreadable during `FindThreadsAsync`, it is silently skipped. Corrupt files do not prevent other threads from being discovered.

## 10. Approval Flow Integration

### 10.1 Principle

Approvals are part of the turn model, not an out-of-band concern owned by individual channels.

### 10.2 Normative Behavior

When a tool execution requires approval, Session Core must:

- emit an approval request event tied to the active turn
- pause the affected execution path until resolution or timeout
- record the approval outcome in the turn history
- resume or reject the operation accordingly

The adapter is responsible only for presenting the request and returning the decision.

### 10.3 Constraints

- Approval UX remains channel-specific.
- Non-interactive server-managed channels may auto-approve.
- API and AG-UI may continue using their own approval mechanisms because they are outside Session Core.

## 11. Implementation Status

### 11.1 Adopted Scope

The Session Protocol is now the active execution path for all **server-managed** channels:

- CLI
- ACP
- QQ
- WeCom
- GitHubTracker

These channels create threads, submit turns through `ISessionService`, consume `SessionEvent`s, and persist state via rollout files plus `.craft/state.db`.

### 11.2 Explicit Exemptions

The following channels are intentionally **outside** Session Core:

- **API**: Retains `MapOpenAIChatCompletions` with request-scoped history and framework-managed streaming.
- **AG-UI**: Retains `MapAGUI` with client-owned history and frontend-driven thread state.

Both rely on `TracingChatClient` and `TokenUsageStore` for observability rather than `ISessionService`.

### 11.3 Cross-Channel Resume Status

Cross-channel resume works for channels that share the same identity shape:

- **CLI ↔ ACP** share `UserId = "local"` and `ChannelContext = null`, so they naturally share one thread pool.
- **QQ** and **WeCom** remain isolated by `ChannelContext`, which is the intended behavior for social conversations.
- **API** and **AG-UI** do not participate because they do not create server-managed threads.

## 12. Failure Model

### 12.1 Failure Classes

#### Turn-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Agent Exception** | `RunStreamingAsync` throws | Create Error Item. Set Turn status = Failed. Emit `turn/failed`. Save partial state. |
| **Tool Execution Error** | A tool throws during `FunctionInvokingChatClient` processing | The error is captured by `FunctionInvokingChatClient` as a `FunctionResultContent` with error. A `ToolResult` Item is created with `success = false`. The agent decides whether to retry or fail. |
| **Approval Timeout** | Adapter does not resolve approval within timeout | Reject the approval. Create Error Item noting timeout. Tool receives rejection. Agent may continue or fail. |
| **Turn Timeout** | Turn exceeds configurable time limit | Cancel the `CancellationToken`. Create Error Item. Set Turn status = Failed. |
| **Cancellation** | Adapter calls `CancelTurn` | Cancel the `CancellationToken`. Set Turn status = Cancelled. Save partial state. |
| **Prompt Hook Blocked** | PrePrompt hook returns `Blocked = true` | Create Error Item with block reason. Set Turn status = Failed. No agent invocation occurs. |

#### Thread-Level Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Resume Failed (file missing)** | Thread file not found on resume | Return error to adapter. Adapter informs user. |
| **Resume Failed (session corrupt)** | Agent Session file cannot be deserialized | Return error to adapter. Offer to start a new Thread. |
| **Concurrent Turn** | Adapter calls `SubmitInput` while a Turn is Running | Return error to adapter. Adapter should queue or reject the message. |

#### Persistence Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Save Failed** | Disk write error after Turn completes | Log error. In-memory state preserved. Next operation retries save. Turn result is still delivered to adapter (events already emitted). |
| **Metadata Corrupt** | `threads` metadata table unreadable or inconsistent | Return error for discovery operations and log warning. |
| **Disk Full** | No space for new rollout records or SQLite writes | Return error on CreateThread/SubmitInput. Adapter informs user. |

#### Channel Disconnect Failures

| Failure | Trigger | Behavior |
|---------|---------|----------|
| **Adapter Disconnects Mid-Turn** | QQ WebSocket drops, ACP stdio closes | Turn continues to completion. Events are emitted to a dead consumer (buffered and eventually dropped). On reconnect, the adapter can resume the Thread and see the completed Turn's results. |
| **Adapter Never Resolves Approval** | Channel disconnects while WaitingApproval | Approval timeout fires. Approval is rejected. Turn continues. |

### 12.2 Recovery Strategy

- **Turn failures** do not corrupt Thread state. A failed Turn is recorded in the Thread's Turn history. The adapter can submit a new Turn to retry.
- **Persistence failures** are recoverable because Session Core maintains in-memory state and retries on next operation.

### 9.8 Thread Rollback

`RollbackThread(threadId, numTurns)` removes `numTurns` turns from the end of a non-archived Thread. `numTurns` must be at least 1 and no turn in the Thread may be `Running` or `WaitingApproval`.

Rollback appends a canonical rollback record to thread JSONL and updates thread metadata; it does not revert files or other workspace side effects created by tools. After rollback, Session Core rebuilds or invalidates the persisted `AgentSession` from canonical history so future turns use the pruned conversation.
- **Channel disconnects** are transparent to Session Core. Turns run to completion regardless of adapter state. Results are persisted and available on reconnect.

### 12.3 Error Reporting

All failures surface as:
1. An `Error` Item within the Turn (for turn-level failures)
2. An exception returned to the adapter's `SubmitInput`/`ResumeThread` call (for thread-level and persistence failures)
3. A log entry with structured context (`threadId`, `turnId`, error category)

## 13. Test and Validation Matrix

### 13.1 Validation Profiles

- **Core Conformance**: Tests for Session Core types, lifecycle, event emission, persistence. Required for any implementation.
- **Adapter Conformance**: Tests for each channel adapter's integration with Session Core. Required per channel.
- **Cross-Channel Conformance**: Tests for shared thread discovery and resume across compatible identities.

### 13.2 Core Conformance Tests

#### Types and Serialization

- Thread serialization/deserialization round-trip preserves all fields
- Turn serialization preserves Item order
- Item payload schemas validate correctly for each Item type
- Thread ID generation produces unique IDs
- Turn ID sequential numbering is correct within a Thread
- Item ID sequential numbering is correct within a Turn

#### Thread Lifecycle

- `CreateThread` sets correct initial state (Active, timestamps, generated ID)
- `PauseThread` transitions Active → Paused
- `ResumeThread` transitions Paused → Active, updates `LastActiveAt`
- `ArchiveThread` transitions Active → Archived
- `ArchiveThread` on Paused thread succeeds
- `ResumeThread` on Archived thread fails
- `SubmitInput` on Paused thread fails
- `SubmitInput` on Archived thread fails

#### Turn Lifecycle

- `SubmitInput` creates Turn with Running status, UserMessage Item
- Turn completes with Completed status after agent finishes
- Turn fails with Failed status on agent exception
- `CancelTurn` sets Cancelled status
- `SubmitInput` while Turn is Running returns error
- Turn with approval: Running → WaitingApproval → Running → Completed
- Approval timeout: WaitingApproval → approval rejected → Turn continues

#### Event Emission

- `turn/started` emitted on `SubmitInput`
- `item/started` emitted for each Item creation
- `item/delta` emitted for streaming AgentMessage content
- `item/delta` emitted for streaming CommandExecution output when shell streaming is enabled
- `item/completed` emitted when Item is finalized
- `turn/completed` emitted after all Items are complete
- `turn/failed` emitted on error
- `approval/requested` emitted when approval needed
- `approval/resolved` emitted when approval resolved
- Event ordering is causal (started before delta before completed)
- `turn/completed` is always the last event for a Turn

#### Persistence

- Thread file written after Turn completes
- Thread file loaded correctly on resume
- Agent Session file round-trip preserves conversation history
- Thread index updated on create, resume, pause, archive
- Thread index rebuilt from files when missing

### 13.3 Adapter Conformance Tests (Per-Channel)

For each migrated channel:

- User message → Turn created → response delivered to user
- Streaming deltas delivered incrementally (for channels that support it)
- Tool calls visible to user (for channels that render them)
- Approval request presented to user (for channels with interactive approval)
- Approval response routed back to Session Core
- Thread created with correct `OriginChannel` and `UserId`
- Thread resumed correctly (conversation context preserved)
- Cancellation works (user can cancel a running Turn)
- Channel disconnect does not crash Session Core

### 13.4 Cross-Channel Conformance Tests

- Thread created by Channel A is discoverable by Channel B
- Thread resumed by Channel B has full conversation context from Channel A
- New Turn on resumed Thread produces correct Items
- Thread metadata from Channel A is preserved after Channel B Turn

### 13.5 Per-Session Configuration Conformance Tests (Section 16)

- Thread created with MCP servers connects them and adds tools to agent
- Mode switch via `SetThreadMode` rebuilds agent tool set
- Thread archive disconnects per-thread MCP servers
- Thread without configuration uses workspace defaults
- ACP extensions recorded in `Thread.Configuration.Extensions`
- Simulated host restart (new Session Core instance, same persistence): a thread with non-null `Thread.Configuration` is loaded from disk; `EnsureThreadLoaded` (or turn start) hydrates the per-thread agent so turns do not fall back to workspace-default agent-only behavior

### 13.6 Social Channel Conformance Tests (Section 17)

- Group session Thread allows different `SenderContext` per Turn
- Permission check at adapter level rejects unauthorized users before `SubmitInput`
- `/stop` command maps to `CancelTurn` and cancels running Turn
- `/new` command archives current Thread and creates new one
- Slash commands not exposed as Items (adapter-local operations)

## 14. Validation Priorities

This specification no longer tracks implementation phases or completed checklists. The remaining validation work is:

- Expand automated **Core Conformance** coverage for lifecycle, persistence, and failure handling.
- Add per channel **Adapter Conformance** coverage for CLI, ACP, QQ, WeCom, and GitHubTracker.
- Add **Cross-Channel Conformance** coverage for the CLI ↔ ACP shared thread pool.
- Add **Per-Session Configuration** coverage for ACP-specific mode and MCP behavior.
- Add **Social Channel Conformance** coverage for sender context, approval routing, and slash commands.

The purpose of this section is to define ongoing verification targets, not to duplicate project-management to-do lists.

---

## 15. Channel-Specific History Boundaries

The Session Protocol applies only to server-managed channels:

- CLI
- ACP
- QQ
- WeCom
- GitHubTracker

For these channels, Session Core loads persisted session state, executes the turn, emits `SessionEvent`s, and persists updated thread/session state afterward.

### 15.1 API and AG-UI Are Explicitly Out of Scope

API and AG-UI do **not** integrate with `ISessionService`.

- **API** keeps its existing OpenAI-compatible request model.
- **AG-UI** keeps its frontend-owned thread state and `MapAGUI` event pipeline.

These channels do not create Session Protocol threads.

### 15.2 Observability for Exempt Channels

Observability for API and AG-UI is provided by the existing middleware stack rather than Session Core:

- `TracingChatClient` records requests, responses, tool calls, and errors.
- `TokenUsageStore` records per-request token usage.

### 15.3 Resume Semantics

Cross-channel resume applies only to **server-managed** threads.

- **CLI ↔ ACP** resume works because both participate in Session Core and share the same identity shape.
- **QQ** and **WeCom** remain isolated by `ChannelContext`.
- **API** and **AG-UI** do not participate in server-side thread discovery or resume.

---

## 16. Per-Session Agent Configuration

### 16.1 Principle

Thread configuration belongs to the thread model rather than to any individual adapter.

### 16.2 Thread Configuration

Each thread may carry a `Configuration` object. This is a thread-owned model, not channel-owned state, and the same shape applies across CLI, AppServer, external adapters, and other hosts.

```
ThreadConfiguration
├── McpServers: McpServerConfig[]?               // Per-thread MCP server connections
├── Mode: string                                 // Agent mode: "agent", "plan", etc. (default: "agent")
├── Extensions: string[]?                        // Active extension prefixes, e.g. ["_unity"]
├── CustomTools: string[]?                       // Additional tool names to enable
├── Model: string?                               // Optional per-thread model override
├── WorkspaceOverride: string?                   // Alternate workspace root for this thread
├── ToolProfile: string?                         // Named tool profile to inject
├── UseToolProfileOnly: bool                     // Use only the profile tools when true
├── AgentInstructions: string?                   // Optional extra system instructions
├── ApprovalPolicy: default|autoApprove|interrupt// Thread-scoped approval behavior
├── AutomationTaskDirectory: string?             // Local automation task directory
└── RequireApprovalOutsideWorkspace: bool?       // Overrides workspace file/shell boundary behavior
```

Approval-related fields are normative:

- `ApprovalPolicy = default` means the thread uses the normal interactive approval path when a tool requests approval.
- `ApprovalPolicy = autoApprove` means approval-gated operations on that thread are auto-accepted by the server.
- `ApprovalPolicy = interrupt` means any approval-gated operation interrupts/cancels the turn instead of prompting.
- `RequireApprovalOutsideWorkspace = true` allows outside-workspace file or shell operations to proceed through the approval service.
- `RequireApprovalOutsideWorkspace = false` rejects outside-workspace file or shell operations without prompting.
- `RequireApprovalOutsideWorkspace = null` falls back to the workspace-level defaults in `AppConfig.Tools.File` and `AppConfig.Tools.Shell`.

When a thread is created or its configuration changes, Session Core recreates the effective agent/tool set from that configuration.

### 16.3 Mode Switching

Mode switching is a thread-level operation:

```
ISessionService.SetThreadMode(threadId: string, mode: string) → void
```

- Changes `Thread.Configuration.Mode`.
- Session Core recreates the agent with the new mode's tool set.
- No Turn is created. This is a metadata operation.
- Emits `thread/statusChanged` event with mode information.

### 16.3.1 Mode-Specific Tool Injection

Each agent mode defines a **mode-specific tool set** that is injected (or removed) when the agent is created for that mode. The `AgentFactory` is responsible for assembling the correct tools based on the mode:

| Mode | Injected Tools | Required Dependency | Removed Tools |
|------|---------------|---------------------|---------------|
| `plan` | `CreatePlan` | `PlanStore` | Tools in the plan-mode deny list (e.g., `TodoWrite`, `UpdateTodos`) |
| `agent` | `UpdateTodos`, `TodoWrite` | `PlanStore` | _(none beyond global deny list)_ |

**`PlanStore` as a Required Dependency**: `PlanStore` provides per-session plan persistence and is required for plan-related tool injection. All hosts that support mode switching **must** supply a `PlanStore` instance to `AgentFactory`. When `PlanStore` is `null`, plan-related tools are silently omitted regardless of the requested mode — this is considered a host configuration error, not a graceful degradation.

**`onPlanUpdated` Callback**: Hosts may optionally supply a plan-update callback to propagate plan state changes to their UX layer (e.g., CLI status panel, ACP notification, Wire notification). The absence of this callback does not affect tool injection; it only disables real-time plan status updates to the client.

**Host Equivalence Requirement**: Every host that exposes `ISessionService` (and therefore mode switching) must construct `AgentFactory` with equivalent mode-critical dependencies. The minimum set is:

- `PlanStore` — required for plan/agent mode tools
- `HookRunner` — optional but recommended for lifecycle hooks

### 16.4 MCP Lifecycle

MCP server connections are thread-scoped, not turn-scoped:

- **Connect**: When a thread is created with `McpServers`, Session Core connects those servers and adds their tools. The same applies when a thread with persisted `McpServers` is prepared for turn execution after a cold load (e.g. via `ResumeThread` from disk or `EnsureThreadLoaded` before `SubmitInput`). Purely read-only operations (`GetThread`, thread discovery) must not connect MCP servers solely because thread metadata was loaded.
- **Disconnect**: When a thread is archived or its MCP configuration changes, Session Core disconnects the previous servers.
- **Lifecycle**: MCP connections live as long as the thread remains active.

When `Thread.Configuration.McpServers` is null, workspace-level MCP configuration applies.

### 16.5 ACP Extension Capabilities

ACP-specific capabilities such as extension prefixes are connection-scoped at discovery time but may be recorded in `Thread.Configuration.Extensions` when they affect the thread's effective tool set.

For channels that do not use extension capabilities, `Thread.Configuration.Extensions` is null.

### 16.6 Design Constraints

- Configuration changes do not implicitly create turns.
- Configuration must be persisted with the thread.
- Channels may expose only the subset of configuration that their UX supports.

---

## 17. Social Channel Patterns

### 17.1 Group Sessions (Multi-User)

QQ-style group sessions are supported without changing the Thread / Turn / Item model:

- `Thread.UserId` for group sessions is **null or a group identifier** (e.g., `qq_group:12345`), not an individual user.
- Each Turn's `Input` Item can carry per-message sender information in its payload.
- The adapter may inject sender context into the prompt.

Add to `UserMessage` payload:

```
{
  "text": string,
  "nativeInputParts": [InputPart],
  "materializedInputParts": [InputPart],
  "senderId": string,          // Individual sender within a group session (nullable)
  "senderName": string,        // Display name of the sender (nullable)
  "images": [                  // Optional local image metadata for UI rehydration
    {
      "path": string,
      "mimeType": string,
      "fileName": string
    }
  ]
}
```

Session Core still treats the thread as a single execution context; sender identity is carried at the turn level.

### 17.2 Permission and Role System

Permissions are an adapter-level concern. Typical roles include `Unauthorized`, `Whitelisted`, and `Admin`. They affect:

- Whether a user can chat at all
- Whether tools can write to the workspace
- Whether a user can approve operations
- Whether a user can use slash commands like `/stop`, `/new`

The adapter's responsibilities:
1. Check permissions before calling `SubmitInput` (reject unauthorized users).
2. Set `ApprovalContext` with the user's role so that `SessionApprovalService` can route appropriately.
3. Filter slash commands by permission level before executing them.

Add `SenderContext` to `SubmitInput`:

```
SubmitInput(threadId, text, senderContext?: SenderContext)

SenderContext
├── SenderId: string           // Individual user ID
├── SenderName: string         // Display name
├── SenderRole: string         // "admin", "whitelisted", "unauthorized"
└── GroupId: string            // Group/chat ID for group sessions (nullable)
```

Session Core records `SenderContext` and passes it through to approval handling. The adapter is responsible for populating it.

### 17.3 Slash Commands

Slash commands are modeled as a managed subsystem with a single server-side command registry for **server-managed commands**:

- Built-in in-process adapters (CLI, QQ, WeCom) call the registry directly.
- Out-of-process adapters use AppServer wire methods (`command/list`, `command/execute`).
- Both paths resolve against the same server-managed command set and permission metadata.

| Command | Maps To | Scope |
|---------|---------|-------|
| `/stop` | `ISessionService.CancelTurn(turnId)` | Session Core |
| `/new` | `ISessionService.ResetConversation(identity)` (archive reusable threads + create fresh thread) | Session Core |
| `/load`, `/sessions` | `ISessionService.FindThreads(identity)` + `ResumeThread` | Session Core |
| `/help` | Managed command metadata listing | Session Core + Adapter rendering |
| `/heartbeat` | `HeartbeatService.TriggerNowAsync()` | AppServer-hosted service |
| `/cron` | Cron management operations | AppServer-hosted service |
| `/debug` | Debug mode toggle operation | AppServer-hosted service |
| Custom commands | `CustomCommandLoader.TryResolve` | Session Core command pipeline |

The registry is authoritative for command discovery, permission hints, and execution routing.
Adapters may still provide platform-specific UX (for example native command menus), but they must not fork server-managed command semantics.
`/clear` is intentionally excluded from Session Core semantics and should be treated as a client-local UI command (clear screen) rather than a thread lifecycle command.
Client-local commands are outside `command/list` and `command/execute`.

### 17.4 Active Run Cancellation

QQ and WeCom use `ActiveRunRegistry` to track and cancel in-flight runs. Under Session Core, this is replaced by:

- Session Core tracks the `CancellationTokenSource` for each Running Turn internally.
- `CancelTurn(turnId)` cancels the token and transitions the Turn to `Cancelled`.
- The adapter maps `/stop` to `CancelTurn` for the current Thread's active Turn.
- `ActiveRunRegistry` is no longer needed — Session Core owns the cancellation lifecycle.

---

## 18. Bidirectional Capabilities

Bidirectional capabilities are outside the session model.

The Session Protocol models conversation state and turn execution. It does not model transport-specific request/response features such as IDE filesystem access, terminal control, extension calls, or API-specific REST flows. Those remain tool- or channel-level concerns. Background terminals follow the same boundary: Session Core records the observable `CommandExecution` Item for the originating tool call, while terminal listing, reading, stdin writes, stopping, and cleanup are exposed by tool/provider or AppServer capabilities.

The design rule is simple:

- Session Core models conversation semantics.
- Adapters and tool providers model transport capabilities.

---

## 19. Wire Protocol (Cross-Language SDK Support)

> **Status**: Specified. See the [DotCraft AppServer Protocol Specification](appserver-protocol.md) for the full definition.

### 19.1 Goal

Expose Session Core over a language-neutral protocol so that non-C# adapters (IDE extensions, web frontends, third-party integrations) can participate in the same server-managed thread model without linking DotCraft.Core directly.

The AppServer wire protocol is specified in [appserver-protocol.md](appserver-protocol.md). That document defines the transport, JSON-RPC message shapes, method surface, event notifications, error handling, and approval request/response mechanics that project this Session Core model to external clients.

### 19.2 External Channel Adapters

The wire protocol also enables out-of-process social channel adapters written in any language. By implementing a Wire Protocol client, a channel adapter gains the full session model — thread lifecycle, streaming events, bidirectional approval — without any C# binding.

This is specified in the [External Channel Adapter Specification](external-channel-adapter.md) (Draft). The key prerequisite for external channels is the WebSocket transport defined in [appserver-protocol.md §15](appserver-protocol.md#15-websocket-transport).

### 19.3 Relationship to Existing API

The AppServer protocol complements, not replaces, `/v1/chat/completions`.

- `/v1/chat/completions` remains the simple client-managed entry point (API channel).
- The AppServer protocol is the server-managed entry point for persistent threads and structured events.
- AG-UI retains its own client-managed transport.

