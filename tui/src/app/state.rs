// AppState — single source of truth for all UI state.
// All mutations happen synchronously in the event loop between frames.

use std::cell::Cell;

use super::token_tracker::TokenTracker;
use crate::wire::types::CommandInfo;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum TurnStatus {
    Idle,
    Running,
    WaitingApproval,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum AgentMode {
    Agent,
    Plan,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum FocusTarget {
    InputEditor,
    ChatView,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum OverlayKind {
    Approval,
    ThreadPicker,
    ModelPicker,
    Help,
}

/// One thread entry fetched from thread/list.
#[derive(Debug, Clone)]
pub struct ThreadEntry {
    pub id: String,
    pub display_name: Option<String>,
    pub status: String,
    pub origin_channel: String,
    pub last_active_at: String,
}

/// State for the thread-picker overlay (/sessions).
#[derive(Debug, Clone)]
pub struct ThreadPickerState {
    pub threads: Vec<ThreadEntry>,
    pub selected: usize,
    pub loading: bool,
    pub error: Option<String>,
}

#[derive(Debug, Clone)]
pub enum ModelCacheState {
    Idle,
    Loading,
    Ready(Vec<String>),
    Error(String),
}

#[derive(Debug, Clone)]
pub struct ModelPickerState {
    pub models: Vec<String>,
    pub selected: usize,
    pub loading: bool,
    pub error: Option<String>,
}

/// Structured state for an in-flight approval request.
#[derive(Debug, Clone)]
pub struct ApprovalState {
    /// JSON-RPC request id — echoed back in the response.
    pub request_id: serde_json::Value,
    /// "shell" or "file"
    pub approval_type: String,
    /// For shell: the command. For file: "read" / "write" / "edit" / "list".
    pub operation: String,
    /// For shell: working directory. For file: the file path.
    pub target: String,
    pub reason: Option<String>,
    /// Currently highlighted decision (0-4).
    pub selected: usize,
}

/// A finalized conversation entry shown in the chat history.
#[derive(Debug, Clone)]
pub enum HistoryEntry {
    UserMessage {
        text: String,
    },
    AgentMessage {
        text: String,
    },
    ToolCall {
        /// Id from wire payload `callId`; matches `toolResult` to the correct tool call.
        call_id: String,
        name: String,
        args: String,
        result: Option<String>,
        /// True when the tool returned successfully (payload.success == true).
        success: bool,
        /// How long the tool call took (from item/started to toolExecution/toolResult completion).
        duration: Option<std::time::Duration>,
    },
    Error {
        message: String,
    },
    SystemInfo {
        message: String,
    },
}

/// State for the currently active (streaming) agent turn.
#[derive(Debug, Default)]
pub struct StreamingState {
    pub message_buffer: String,
    pub reasoning_buffer: String,
    pub is_reasoning: bool,
    pub active_tools: Vec<ActiveToolCall>,
    pub active_command_executions: Vec<ActiveCommandExecution>,
}

impl StreamingState {
    pub fn clear(&mut self) {
        self.message_buffer.clear();
        self.reasoning_buffer.clear();
        self.is_reasoning = false;
        self.active_tools.clear();
        self.active_command_executions.clear();
    }
}

#[derive(Debug, Clone)]
pub struct ActiveToolCall {
    pub call_id: String,
    pub tool_name: String,
    pub arguments: String,
    pub completed: bool,
    pub result: Option<String>,
    /// Whether the tool completed successfully (from payload.success).
    pub success: bool,
    /// When this tool call started (set on item/started).
    pub started_at: std::time::Instant,
    /// How long this call took (set on toolExecution/toolResult completion).
    pub duration: Option<std::time::Duration>,
}

#[derive(Debug, Clone)]
pub struct ActiveCommandExecution {
    pub item_id: String,
    pub call_id: Option<String>,
    pub command: String,
    pub working_directory: Option<String>,
    pub source: Option<String>,
    pub aggregated_output: String,
    pub completed: bool,
    pub started_at: std::time::Instant,
    pub duration: Option<std::time::Duration>,
    pub exit_code: Option<i32>,
    pub status: String,
}

#[derive(Debug, Clone)]
pub struct SubAgentEntry {
    pub label: String,
    pub current_tool: Option<String>,
    pub input_tokens: i64,
    pub output_tokens: i64,
    pub is_completed: bool,
}

#[derive(Debug, Clone)]
pub struct PlanTodo {
    pub id: String,
    pub content: String,
    pub priority: String,
    pub status: String,
}

#[derive(Debug, Clone)]
pub struct PlanSnapshot {
    pub title: String,
    pub overview: String,
    pub todos: Vec<PlanTodo>,
}

#[derive(Debug, Clone)]
pub struct NotificationEntry {
    pub source: String,
    pub job_name: Option<String>,
    pub result: Option<String>,
    pub error: Option<String>,
    /// Unix timestamp (ms) when this notification should auto-dismiss.
    pub dismiss_at_ms: i64,
}

#[derive(Debug, Clone)]
pub struct SystemStatusInfo {
    pub kind: String,
    pub message: Option<String>,
}

/// State for the slash command completion popup.
#[derive(Debug, Clone)]
pub struct CommandPopupState {
    /// Filtered list of (command, description) pairs.
    pub items: Vec<(String, String)>,
    /// Currently highlighted index.
    pub selected: usize,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SlashCommandDescriptor {
    pub name: String,
    pub description: String,
    pub category: String,
}

impl SlashCommandDescriptor {
    pub fn new(name: impl Into<String>, description: impl Into<String>, category: impl Into<String>) -> Self {
        Self {
            name: name.into(),
            description: description.into(),
            category: category.into(),
        }
    }
}

pub struct AppState {
    // Connection
    pub connected: bool,

    // Identity
    pub workspace_path: String,

    // Thread
    pub current_thread_id: Option<String>,
    pub current_thread_name: Option<String>,
    pub current_model_override: Option<String>,
    pub pending_model_override: Option<String>,
    pub workspace_model: Option<String>,

    // Turn
    pub turn_status: TurnStatus,
    /// Server turn id from `turn/started` (required for `turn/interrupt`).
    pub current_turn_id: Option<String>,
    /// Set when a turn starts; used by StatusIndicator for elapsed time display.
    pub turn_started_at: Option<std::time::Instant>,
    pub history: Vec<HistoryEntry>,
    pub streaming: StreamingState,

    // SubAgents
    pub subagent_entries: Vec<SubAgentEntry>,
    pub last_subagent_entries: Vec<SubAgentEntry>,

    // Plan
    pub plan: Option<PlanSnapshot>,

    // Tokens
    pub token_tracker: TokenTracker,

    // System events
    pub system_status: Option<SystemStatusInfo>,

    // UI
    pub mode: AgentMode,
    pub focus: FocusTarget,
    pub scroll_offset: usize,
    pub at_bottom: bool,
    /// Height of the chat viewport in terminal rows, written each frame by ChatView.
    /// Cell<usize> allows mutation through &AppState so ChatView (which holds &AppState)
    /// can update it during rendering for use by the input router.
    pub last_viewport_height: Cell<usize>,

    // Phase 2: reasoning visibility toggle
    pub show_reasoning: bool,

    // Phase 2: monotonic tick counter for spinner animation (incremented per frame)
    pub tick_count: u64,

    // Input
    pub input_text: String,
    pub input_cursor: usize,
    pub input_history: Vec<String>,
    pub input_history_pos: Option<usize>,
    /// Messages queued to be sent after the current turn completes (via Tab key).
    pub pending_input: Vec<String>,

    // Notifications
    pub notifications: std::collections::VecDeque<NotificationEntry>,

    // Pending approval (Some = ApprovalOverlay shown, None = no overlay)
    pub pending_approval: Option<ApprovalState>,
    // Thread-picker overlay state (/sessions command)
    pub thread_picker: Option<ThreadPickerState>,
    // Model-picker overlay state (/model command)
    pub model_picker: Option<ModelPickerState>,
    // One-shot model catalog cache.
    pub model_cache: ModelCacheState,
    // Which overlay is currently rendering on top of the base UI
    pub active_overlay: Option<OverlayKind>,

    // Slash command completion popup
    pub command_popup: Option<CommandPopupState>,
    pub server_commands: Vec<CommandInfo>,
    pub command_catalog: Vec<SlashCommandDescriptor>,

    // Ctrl+C double-press quit detection
    pub last_interrupt_at: Option<std::time::Instant>,
}

impl AppState {
    pub fn new(workspace_path: String) -> Self {
        Self {
            connected: false,
            workspace_path,
            current_thread_id: None,
            current_thread_name: None,
            current_model_override: None,
            pending_model_override: None,
            workspace_model: None,
            turn_status: TurnStatus::Idle,
            current_turn_id: None,
            turn_started_at: None,
            history: Vec::new(),
            streaming: StreamingState::default(),
            subagent_entries: Vec::new(),
            last_subagent_entries: Vec::new(),
            plan: None,
            token_tracker: TokenTracker::new(),
            system_status: None,
            mode: AgentMode::Agent,
            focus: FocusTarget::InputEditor,
            scroll_offset: 0,
            at_bottom: true,
            // A small non-zero fallback keeps first PageUp/PageDown usable
            // before ChatView has rendered and measured viewport height.
            last_viewport_height: Cell::new(10),
            show_reasoning: true,
            tick_count: 0,
            input_text: String::new(),
            input_cursor: 0,
            input_history: Vec::new(),
            input_history_pos: None,
            pending_input: Vec::new(),
            notifications: std::collections::VecDeque::new(),
            pending_approval: None,
            thread_picker: None,
            model_picker: None,
            model_cache: ModelCacheState::Idle,
            active_overlay: None,
            command_popup: None,
            server_commands: Vec::new(),
            command_catalog: Vec::new(),
            last_interrupt_at: None,
        }
    }

    /// Returns the number of logical lines in the input text.
    /// str::lines() ignores a trailing newline, so we count '\n' directly:
    /// N newlines always means N+1 lines.
    pub fn input_line_count(&self) -> usize {
        if self.input_text.is_empty() {
            1
        } else {
            self.input_text.chars().filter(|&c| c == '\n').count() + 1
        }
    }
}
