// Maps incoming Wire Protocol notifications to AppState mutations.
// Implements §6 of specs/tui-client.md.
//
// StreamCollector integration:
//   The event_mapper appends deltas to `streaming.message_buffer`.
//   The actual StreamCollector (which needs Theme + width) lives in lib.rs and is
//   driven from the event loop after each delta to produce pre-rendered lines.
//   This keeps event_mapper free of rendering dependencies.

use crate::{
    app::state::{
        ActiveToolCall, AppState, HistoryEntry, PlanSnapshot, PlanTodo, StreamingState,
        SubAgentEntry, SystemStatusInfo, TurnStatus,
    },
    wire::types::JsonRpcMessage,
};
// Note: ApprovalState is parsed in lib.rs handle_server_request (needs the JSON-RPC request id).
// event_mapper only handles the resolved notification here.

/// Process one incoming wire message and mutate AppState accordingly.
/// Returns true if the message was handled, false if it was unknown/ignored.
pub fn apply(state: &mut AppState, msg: &JsonRpcMessage) -> bool {
    let method = match &msg.method {
        Some(m) => m.as_str(),
        None => return false, // Response message; handled separately by client.
    };

    let params = msg.params.as_ref().unwrap_or(&serde_json::Value::Null);

    match method {
        // ── Turn lifecycle ────────────────────────────────────────────────
        "turn/started" => {
            state.current_turn_id = params
                .get("turn")
                .and_then(|t| t.get("id"))
                .and_then(|v| v.as_str())
                .map(|s| s.to_string());
            state.turn_status = TurnStatus::Running;
            state.turn_started_at = Some(std::time::Instant::now());
            state.streaming.clear();
            state.subagent_entries.clear();
            state.token_tracker.reset();
            // Auto-scroll to bottom on new turn start.
            state.at_bottom = true;
            true
        }
        "turn/completed" => {
            finalize_streaming(state);
            state.turn_status = TurnStatus::Idle;
            state.current_turn_id = None;
            state.turn_started_at = None;
            if let Some(usage) = params.get("turn").and_then(|t| t.get("tokenUsage")) {
                if let (Some(inp), Some(out)) = (
                    usage.get("inputTokens").and_then(|v| v.as_i64()),
                    usage.get("outputTokens").and_then(|v| v.as_i64()),
                ) {
                    // Only use turn/completed tokenUsage if no item/usage/delta events
                    // were received (fallback for servers that don't send deltas).
                    // When deltas are received, the tracker already holds the correct total
                    // (per appserver-protocol.md §6.6: sum of deltas == tokenUsage).
                    if state.token_tracker.input_tokens == 0 && state.token_tracker.output_tokens == 0 {
                        state.token_tracker.add(inp, out);
                    }
                }
            }
            true
        }
        "turn/failed" => {
            let err = params
                .get("error")
                .and_then(|v| v.as_str())
                .unwrap_or("unknown error")
                .to_string();
            finalize_streaming(state);
            state.history.push(HistoryEntry::Error { message: err });
            state.turn_status = TurnStatus::Idle;
            state.current_turn_id = None;
            state.turn_started_at = None;
            true
        }
        "turn/cancelled" => {
            finalize_streaming(state);
            state.turn_status = TurnStatus::Idle;
            state.current_turn_id = None;
            state.turn_started_at = None;
            true
        }

        // ── Item lifecycle ────────────────────────────────────────────────
        "item/started" => {
            let item = params.get("item").unwrap_or(&serde_json::Value::Null);
            let item_type = item.get("type").and_then(|v| v.as_str()).unwrap_or("");
            match item_type {
                "approvalRequest" => {
                    // Set turn status; the actual ApprovalState is built in
                    // lib.rs::handle_server_request when item/approval/request arrives.
                    state.turn_status = TurnStatus::WaitingApproval;
                }
                "toolCall" => {
                    // Fields are nested inside item.payload per the wire protocol spec.
                    // Fall back to item itself to handle both flat and nested formats.
                    let payload = item.get("payload").unwrap_or(item);
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let tool_name = payload
                        .get("toolName")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let arguments = payload
                        .get("arguments")
                        .map(|v| {
                            if v.is_string() {
                                v.as_str().unwrap_or("").to_string()
                            } else {
                                serde_json::to_string(v).unwrap_or_default()
                            }
                        })
                        .unwrap_or_default();
                    state.streaming.active_tools.push(ActiveToolCall {
                        call_id,
                        tool_name,
                        arguments,
                        completed: false,
                        result: None,
                        success: true,
                        started_at: std::time::Instant::now(),
                        duration: None,
                    });
                }
                _ => {}
            }
            true
        }

        "item/agentMessage/delta" => {
            if let Some(delta) = params.get("delta").and_then(|v| v.as_str()) {
                state.streaming.message_buffer.push_str(delta);
                state.streaming.is_reasoning = false;
                // Note: StreamCollector is updated in lib.rs after this returns,
                // since it needs Theme + width for rendering.
                state.at_bottom = true; // auto-scroll while streaming
            }
            true
        }

        "item/reasoning/delta" => {
            if let Some(delta) = params.get("delta").and_then(|v| v.as_str()) {
                state.streaming.reasoning_buffer.push_str(delta);
                state.streaming.is_reasoning = true;
                state.at_bottom = true;
            }
            true
        }

        "item/completed" => {
            let item = params.get("item").unwrap_or(&serde_json::Value::Null);
            let item_type = item.get("type").and_then(|v| v.as_str()).unwrap_or("");
            match item_type {
                "agentMessage" => {
                    let text = std::mem::take(&mut state.streaming.message_buffer);
                    if !text.is_empty() {
                        state.history.push(HistoryEntry::AgentMessage { text });
                    }
                }
                "toolCall" | "toolResult" => {
                    // The wire protocol sends two separate item/completed events per tool:
                    //   1. type="toolCall"   — the call completed; payload has no result
                    //   2. type="toolResult" — the result arrived; payload has the result text
                    //
                    // We handle them in order:
                    //   - toolCall:   update the ActiveToolCall and move it to history
                    //   - toolResult: if the tool is still in active_tools, update it there;
                    //                 if it was already moved to history, patch the history entry
                    let payload = item.get("payload").unwrap_or(item);
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    let result_text = payload
                        .get("result")
                        .and_then(|v| v.as_str())
                        .map(str::to_string)
                        .or_else(|| {
                            payload
                                .get("output")
                                .and_then(|v| v.as_str())
                                .map(str::to_string)
                        });

                    // Update the ActiveToolCall if it is still in the streaming list.
                    if let Some(tool) = state
                        .streaming
                        .active_tools
                        .iter_mut()
                        .find(|t| t.call_id == call_id)
                    {
                        let now = std::time::Instant::now();
                        tool.duration = Some(now.duration_since(tool.started_at));
                        tool.completed = true;
                        tool.success = success;
                        if result_text.is_some() {
                            tool.result = result_text.clone();
                        }
                    }

                    // toolCall completion: move the entry to committed history.
                    if item_type == "toolCall" {
                        if let Some(pos) = state
                            .streaming
                            .active_tools
                            .iter()
                            .position(|t| t.call_id == call_id && t.completed)
                        {
                            let tool = state.streaming.active_tools.remove(pos);
                            state.history.push(HistoryEntry::ToolCall {
                                call_id: tool.call_id.clone(),
                                name: tool.tool_name,
                                args: tool.arguments,
                                result: tool.result,
                                success: tool.success,
                                duration: tool.duration,
                            });
                        }
                    }

                    // toolResult completion: the result arrives after the toolCall event has
                    // already moved the entry to history. Patch the ToolCall with matching call_id.
                    if item_type == "toolResult" {
                        let still_active = state
                            .streaming
                            .active_tools
                            .iter()
                            .any(|t| t.call_id == call_id);
                        if !still_active {
                            for entry in state.history.iter_mut().rev() {
                                if let HistoryEntry::ToolCall {
                                    call_id: ref id,
                                    result: ref mut r,
                                    success: ref mut s,
                                    ..
                                } = entry
                                {
                                    if id == &call_id && r.is_none() {
                                        *r = result_text;
                                        *s = success;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                _ => {}
            }
            true
        }

        "item/approval/resolved" => {
            state.pending_approval = None;
            state.active_overlay = None;
            if state.turn_status == TurnStatus::WaitingApproval {
                state.turn_status = TurnStatus::Running;
            }
            true
        }

        // ── SubAgent progress ─────────────────────────────────────────────
        "subagent/progress" => {
            if let Some(entries) = params.get("entries").and_then(|v| v.as_array()) {
                state.subagent_entries = entries
                    .iter()
                    .filter_map(|e| {
                        Some(SubAgentEntry {
                            label: e.get("label")?.as_str()?.to_string(),
                            current_tool: e
                                .get("currentTool")
                                .and_then(|v| v.as_str())
                                .map(str::to_string),
                            input_tokens: e
                                .get("inputTokens")
                                .and_then(|v| v.as_i64())
                                .unwrap_or(0),
                            output_tokens: e
                                .get("outputTokens")
                                .and_then(|v| v.as_i64())
                                .unwrap_or(0),
                            is_completed: e
                                .get("isCompleted")
                                .and_then(|v| v.as_bool())
                                .unwrap_or(false),
                        })
                    })
                    .collect();
            }
            true
        }

        // ── Token usage ───────────────────────────────────────────────────
        "item/usage/delta" => {
            let input = params
                .get("inputTokens")
                .and_then(|v| v.as_i64())
                .unwrap_or(0);
            let output = params
                .get("outputTokens")
                .and_then(|v| v.as_i64())
                .unwrap_or(0);
            state.token_tracker.add(input, output);
            true
        }

        // ── System events ─────────────────────────────────────────────────
        "system/event" => {
            let kind = params
                .get("kind")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let message = params
                .get("message")
                .and_then(|v| v.as_str())
                .map(str::to_string);
            match kind.as_str() {
                "compacting" | "consolidating" => {
                    state.system_status = Some(SystemStatusInfo {
                        kind: kind.clone(),
                        message: message.clone(),
                    });
                }
                "compacted" | "compactSkipped" | "consolidated" => {
                    state.system_status = None;
                    if let Some(msg) = message {
                        state.history.push(HistoryEntry::SystemInfo { message: msg });
                    }
                }
                _ => {}
            }
            true
        }

        // ── Plan updates ──────────────────────────────────────────────────
        "plan/updated" => {
            let title = params
                .get("title")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let overview = params
                .get("overview")
                .and_then(|v| v.as_str())
                .unwrap_or("")
                .to_string();
            let todos = params
                .get("todos")
                .and_then(|v| v.as_array())
                .map(|arr| {
                    arr.iter()
                        .filter_map(|t| {
                            Some(PlanTodo {
                                id: t.get("id")?.as_str()?.to_string(),
                                content: t.get("content")?.as_str()?.to_string(),
                                priority: t
                                    .get("priority")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("medium")
                                    .to_string(),
                                status: t
                                    .get("status")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("pending")
                                    .to_string(),
                            })
                        })
                        .collect()
                })
                .unwrap_or_default();
            state.plan = Some(PlanSnapshot { title, overview, todos });
            true
        }

        // ── Job results ───────────────────────────────────────────────────
        "system/jobResult" => {
            use chrono::Utc;
            let dismiss_at_ms = Utc::now().timestamp_millis() + 10_000;
            state.notifications.push_back(crate::app::state::NotificationEntry {
                source: params
                    .get("source")
                    .and_then(|v| v.as_str())
                    .unwrap_or("cron")
                    .to_string(),
                job_name: params
                    .get("jobName")
                    .and_then(|v| v.as_str())
                    .map(str::to_string),
                result: params
                    .get("result")
                    .and_then(|v| v.as_str())
                    .map(str::to_string),
                error: params
                    .get("error")
                    .and_then(|v| v.as_str())
                    .map(str::to_string),
                dismiss_at_ms,
            });
            true
        }

        // ── Thread lifecycle ──────────────────────────────────────────────
        "thread/started" | "thread/resumed" => {
            if let Some(thread) = params.get("thread") {
                state.current_thread_id = thread
                    .get("id")
                    .and_then(|v| v.as_str())
                    .map(str::to_string);
                state.current_thread_name = thread
                    .get("displayName")
                    .and_then(|v| v.as_str())
                    .map(str::to_string);
            }
            true
        }

        "thread/statusChanged" => true,

        _ => false,
    }
}

/// Move any in-flight streaming content into committed history.
fn finalize_streaming(state: &mut AppState) {
    let msg = std::mem::take(&mut state.streaming.message_buffer);
    if !msg.is_empty() {
        state.history.push(HistoryEntry::AgentMessage { text: msg });
    }
    state.streaming = StreamingState::default();
}
