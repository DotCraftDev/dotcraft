pub mod app;
pub mod clipboard;
pub mod hub;
pub mod i18n;
pub mod terminal;
pub mod theme;
pub mod ui;
pub mod wire;

use anyhow::Result;
use crossterm::event::{Event as CrosstermEvent, EventStream, KeyEventKind, MouseEventKind};
use futures::StreamExt;
use std::time::{Duration, Instant};
use tokio::sync::mpsc as tokio_mpsc;
use tokio::time;

use crate::{
    app::{
        commands::{self, LocalSlashCommand, ParsedSlashCommand},
        event_mapper,
        input_router::{self, InputAction, ModelPickerOp, ThreadPickerOp},
        state::{
            AgentMode, AppState, ApprovalState, HistoryEntry, ModelCacheState, ModelPickerState,
            OverlayKind, ThreadEntry, ThreadPickerState, TurnStatus,
        },
    },
    i18n::Strings,
    terminal::{Term, TerminalGuard},
    theme::Theme,
    ui::{
        chat_view::ChatView,
        footer_line::FooterLine,
        input_editor::InputEditor,
        layout,
        overlays::{
            approval::ApprovalOverlay, command_popup::CommandPopup, help::HelpOverlay,
            model_picker::ModelPicker, notification::NotificationToast,
            thread_picker::ThreadPicker,
        },
        status_indicator::StatusIndicator,
        welcome_screen::WelcomeScreen,
    },
    wire::{client::WireClient, transport::Transport},
};

/// Tracks how we're connected to the AppServer for reconnection logic.
#[derive(Clone, Debug)]
#[allow(dead_code)]
enum ConnectionMode {
    LocalHub(String),
    WebSocket(String),
}

/// Async result forwarded from spawned tasks back into the event loop.
enum DeferredResult {
    ThreadListLoaded(Result<serde_json::Value>),
    ThreadHistoryLoaded(Result<serde_json::Value>),
    ModelCatalogLoaded(Result<serde_json::Value>),
}

/// Signals that the WelcomeScreen has been dismissed and chat UI should show.
#[derive(Clone, Debug, PartialEq, Eq)]
enum UiPhase {
    Welcome,
    Chat,
}

/// Resolve the UI language with the following priority:
///   1. Explicit `--lang` CLI flag (highest priority).
///   2. `Language` field in `{workspace}/.craft/config.json`.
///   3. Default: `"en"`.
///
/// config.json values recognised (case-insensitive):
///   "Chinese" | "中文" | "zh" | "zh-cn" -> "zh"
///   "English" | "en"                    -> "en"
fn resolve_language(cli_lang: Option<&str>, workspace_path: Option<&std::path::Path>) -> String {
    // 1. CLI flag wins unconditionally.
    if let Some(lang) = cli_lang {
        return lang.to_string();
    }

    // 2. Try .craft/config.json in the workspace directory.
    if let Some(ws) = workspace_path {
        let config_path = ws.join(".craft").join("config.json");
        if let Ok(content) = std::fs::read_to_string(&config_path) {
            if let Ok(value) = serde_json::from_str::<serde_json::Value>(&content) {
                if let Some(lang_val) = value.get("Language").and_then(|v| v.as_str()) {
                    return match lang_val.to_lowercase().as_str() {
                        "chinese" | "中文" | "zh" | "zh-cn" | "zh_cn" => "zh".to_string(),
                        _ => "en".to_string(),
                    };
                }
            }
        }
    }

    // 3. Default.
    "en".to_string()
}

fn read_workspace_model(workspace_path: &std::path::Path) -> Option<String> {
    let config_path = workspace_path.join(".craft").join("config.json");
    let content = std::fs::read_to_string(config_path).ok()?;
    let value = serde_json::from_str::<serde_json::Value>(&content).ok()?;
    let obj = value.as_object()?;
    for (k, v) in obj {
        if k.eq_ignore_ascii_case("model") {
            let model = v.as_str()?.trim().to_string();
            if model.is_empty() {
                return None;
            }
            return Some(model);
        }
    }
    None
}

/// Entry point called from main.rs.
pub async fn run(
    remote: Option<String>,
    server_bin: Option<String>,
    workspace: Option<String>,
    theme_path: Option<String>,
    lang: Option<String>,
) -> Result<()> {
    // ── 1. Logging ────────────────────────────────────────────────────────
    tracing_subscriber::fmt()
        .with_writer(std::io::stderr)
        .with_env_filter(tracing_subscriber::EnvFilter::from_env("DOTCRAFT_TUI_LOG"))
        .init();

    // ── 2. Theme and i18n ─────────────────────────────────────────────────
    // Resolve the effective workspace path early so theme and language loading
    // can both read .craft/ from it.
    let resolved_workspace: std::path::PathBuf = workspace
        .as_deref()
        .map(std::path::PathBuf::from)
        .or_else(|| std::env::current_dir().ok())
        .unwrap_or_default();
    let workspace_path = Some(resolved_workspace.as_path());

    let cli_theme_path = theme_path.as_deref().map(std::path::Path::new);
    let theme = Theme::resolve(cli_theme_path, workspace_path)?;
    let resolved_lang = resolve_language(lang.as_deref(), workspace_path);
    let strings = i18n::load(&resolved_lang);

    // ── 3. Transport ──────────────────────────────────────────────────────
    let connection_mode = if remote.is_some() {
        ConnectionMode::WebSocket(remote.clone().unwrap())
    } else {
        ConnectionMode::LocalHub(server_bin.clone().unwrap_or_else(|| "dotcraft".to_string()))
    };

    let transport = match &connection_mode {
        #[cfg(feature = "websocket")]
        ConnectionMode::WebSocket(url) => {
            tracing::info!("Connecting to remote AppServer: {url}");
            Transport::connect_ws(url).await?
        }
        #[cfg(not(feature = "websocket"))]
        ConnectionMode::WebSocket(_) => {
            anyhow::bail!(
                "--remote requires the 'websocket' feature. \
                 Rebuild with: cargo build --features websocket"
            )
        }
        ConnectionMode::LocalHub(bin) => {
            tracing::info!("Ensuring local AppServer through Hub: {bin} hub");
            let ws_url = hub::ensure_appserver(&resolved_workspace, bin).await?;
            tracing::info!("Connecting to Hub-managed AppServer: {ws_url}");
            #[cfg(feature = "websocket")]
            {
                Transport::connect_ws(&ws_url).await?
            }
            #[cfg(not(feature = "websocket"))]
            {
                anyhow::bail!(
                    "Local Hub mode requires the 'websocket' feature. \
                     Rebuild with: cargo build --features websocket"
                )
            }
        }
    };

    // ── 4. Wire client + handshake ────────────────────────────────────────
    let mut wire = WireClient::spawn(transport);
    wire.initialize().await?;
    tracing::info!(
        "Connected to DotCraft AppServer v{}",
        wire.server_info
            .as_ref()
            .map(|i| i.version.as_str())
            .unwrap_or("?")
    );

    // ── 5. Terminal init ──────────────────────────────────────────────────
    let mut terminal = terminal::init()?;
    let _guard = TerminalGuard;

    // ── 6. AppState ───────────────────────────────────────────────────────
    let ws_path = resolved_workspace.to_string_lossy().into_owned();
    let mut state = AppState::new(ws_path.clone());
    state.connected = true;
    state.workspace_model = read_workspace_model(&resolved_workspace);
    state.command_catalog = commands::merge_command_catalog(&state.server_commands);
    if let Err(e) = refresh_command_catalog(&mut wire, &mut state, &resolved_lang).await {
        tracing::warn!("Failed to load command catalog: {e}");
        state.history.push(HistoryEntry::Error {
            message: format!("Failed to load command catalog: {e}"),
        });
    }

    // ── 7. Event loop (WelcomeScreen shown first, then chat UI) ─────────
    run_event_loop(
        &mut terminal,
        &mut wire,
        &mut state,
        &theme,
        &strings,
        &resolved_lang,
        &connection_mode,
    )
    .await?;

    Ok(())
}

// ── Event loop ────────────────────────────────────────────────────────────

async fn run_event_loop(
    terminal: &mut Term,
    wire: &mut WireClient,
    state: &mut AppState,
    theme: &Theme,
    strings: &Strings,
    language: &str,
    #[allow(unused_variables)] conn_mode: &ConnectionMode,
) -> Result<()> {
    let mut tick = time::interval(Duration::from_millis(16)); // ~60 fps
    tick.set_missed_tick_behavior(time::MissedTickBehavior::Skip);

    let mut event_stream = EventStream::new();

    let (deferred_tx, mut deferred_rx) = tokio_mpsc::unbounded_channel::<DeferredResult>();

    if wire.capabilities.model_catalog_management.unwrap_or(false) {
        state.model_cache = ModelCacheState::Loading;
        if let Err(e) = spawn_model_catalog_load(wire, &deferred_tx).await {
            state.model_cache = ModelCacheState::Error(format!("Failed to load models: {e}"));
        }
    }

    // Show the WelcomeScreen until a key is pressed or the connection is confirmed ready.
    let mut ui_phase = UiPhase::Welcome;

    loop {
        tokio::select! {
            // ── Wire messages ─────────────────────────────────────────────
            Some(msg_result) = wire.recv() => {
                match msg_result {
                    Err(e) => {
                        tracing::warn!("Wire error: {e}");
                        state.connected = false;

                        #[cfg(feature = "websocket")]
                        if let ConnectionMode::WebSocket(url) = conn_mode {
                            state.history.push(HistoryEntry::SystemInfo {
                                message: format!("Connection lost: {e}. Reconnecting..."),
                            });
                            draw(terminal, state, theme, strings)?;

                            match reconnect_ws(url, state, terminal, theme, strings, &mut event_stream).await {
                                Ok(new_wire) => {
                                    *wire = new_wire;
                                    state.connected = true;
                                    if wire.capabilities.model_catalog_management.unwrap_or(false) {
                                        state.model_cache = ModelCacheState::Loading;
                                        let _ = spawn_model_catalog_load(wire, &deferred_tx).await;
                                    } else {
                                        state.model_cache = ModelCacheState::Idle;
                                    }
                                    if let Err(e) = refresh_command_catalog(wire, state, language).await {
                                        tracing::warn!("Failed to refresh command catalog after reconnect: {e}");
                                        state.history.push(HistoryEntry::Error {
                                            message: format!("Failed to refresh command catalog: {e}"),
                                        });
                                    }
                                    if let Some(ref tid) = state.current_thread_id {
                                        let _ = wire.notify("thread/subscribe", serde_json::json!({
                                            "threadId": tid,
                                            "replayRecent": true,
                                        })).await;
                                    }
                                    state.history.push(HistoryEntry::SystemInfo {
                                        message: "Reconnected to AppServer.".to_string(),
                                    });
                                    continue;
                                }
                                Err(e) => {
                                    state.history.push(HistoryEntry::Error {
                                        message: format!("Reconnection failed: {e}"),
                                    });
                                    break;
                                }
                            }
                        }

                        // Local Hub mode or websocket feature disabled: fatal disconnect.
                        state.history.push(HistoryEntry::Error {
                            message: format!("Connection error: {e}"),
                        });
                        break;
                    }
                    Ok(msg) => {
                        if wire.resolve_response(&msg) {
                            // handled internally
                        } else if is_server_request(&msg) {
                            handle_server_request(wire, state, msg).await?;
                        } else {
                            event_mapper::apply(state, &msg);
                            // Auto-submit first queued message after a turn completes.
                            if state.turn_status == TurnStatus::Idle {
                                if let Some(queued) = state.pending_input.first().cloned() {
                                    state.pending_input.remove(0);
                                    submit_turn(wire, state, queued).await?;
                                }
                            }
                        }
                    }
                }
            }

            // ── Deferred async results ───────────────────────────────────
            Some(deferred) = deferred_rx.recv() => {
                handle_deferred_result(state, strings, deferred);
            }

            // ── Terminal events ───────────────────────────────────────────
            Some(evt_result) = event_stream.next() => {
                match evt_result {
                    Err(e) => tracing::warn!("Terminal event error: {e}"),
                    Ok(evt) => {
                        // Any key press dismisses the WelcomeScreen.
                        if ui_phase == UiPhase::Welcome {
                            if let crossterm::event::Event::Key(k) = &evt {
                                if k.kind != crossterm::event::KeyEventKind::Release {
                                    ui_phase = UiPhase::Chat;
                                }
                            }
                        }
                        if ui_phase == UiPhase::Chat {
                            if handle_terminal_event(terminal, wire, state, theme, strings, &deferred_tx, evt).await? {
                                break;
                            }
                        }
                    }
                }
            }

            // ── Tick: redraw ──────────────────────────────────────────────
            _ = tick.tick() => {
                state.tick_count = state.tick_count.wrapping_add(1);
                expire_notifications(state);
                // WelcomeScreen stays until the user presses any key (see key handler above).
                // No auto-dismiss — we want the user to see it before they start typing.
                if ui_phase == UiPhase::Welcome {
                    draw_welcome(terminal, state, theme, strings, env!("CARGO_PKG_VERSION"))?;
                } else {
                    draw(terminal, state, theme, strings)?;
                }
            }
        }
    }

    Ok(())
}

// ── Terminal event handler ────────────────────────────────────────────────

async fn handle_terminal_event(
    terminal: &mut Term,
    wire: &mut WireClient,
    state: &mut AppState,
    _theme: &Theme,
    strings: &Strings,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    evt: CrosstermEvent,
) -> Result<bool> {
    match evt {
        CrosstermEvent::Key(key) => {
            if key.kind == KeyEventKind::Release {
                return Ok(false);
            }

            // When an overlay is active it captures all key events.
            if let Some(overlay) = &state.active_overlay.clone() {
                match overlay {
                    OverlayKind::Approval => {
                        let action = input_router::handle_approval_overlay(state, key);
                        if let InputAction::ApprovalDecision(decision) = action {
                            if let Some(approval) = state.pending_approval.take() {
                                wire.respond(
                                    approval.request_id,
                                    serde_json::json!({ "decision": decision }),
                                )
                                .await?;
                            }
                            state.active_overlay = None;
                        }
                    }
                    OverlayKind::ThreadPicker => {
                        let action = input_router::handle_thread_picker(state, key);
                        handle_thread_picker_action(wire, state, deferred_tx, action).await?;
                    }
                    OverlayKind::ModelPicker => {
                        let action = input_router::handle_model_picker(state, key);
                        handle_model_picker_action(wire, state, action).await?;
                    }
                    OverlayKind::Help => {
                        let action = input_router::handle_help_overlay(key);
                        if matches!(action, InputAction::CloseOverlay) {
                            state.active_overlay = None;
                        }
                    }
                }
                return Ok(false);
            }

            let action = input_router::handle_key(state, key);
            match action {
                InputAction::SubmitTurn(text) => {
                    if text.is_empty() {
                        return Ok(false);
                    }
                    if let Some(cmd) = commands::parse(&text) {
                        let quit =
                            handle_slash_command(wire, state, strings, deferred_tx, cmd).await?;
                        if quit {
                            return Ok(true);
                        }
                        terminal.clear()?;
                    } else {
                        state.streaming.clear();
                        submit_turn(wire, state, text).await?;
                    }
                }
                InputAction::Interrupt => {
                    if handle_interrupt(wire, state).await? {
                        return Ok(true);
                    }
                }
                InputAction::SoftInterrupt => {
                    if state.turn_status == TurnStatus::Running
                        || state.turn_status == TurnStatus::WaitingApproval
                    {
                        if let Some(thread_id) = &state.current_thread_id.clone() {
                            let turn_id = state.current_turn_id.clone().unwrap_or_default();
                            let params = serde_json::json!({
                                "threadId": thread_id,
                                "turnId": turn_id
                            });
                            let _ = wire.send_request("turn/interrupt", params).await;
                        }
                    }
                }
                InputAction::Quit => return Ok(true),
                InputAction::OpenHelp => {
                    state.active_overlay = Some(OverlayKind::Help);
                }
                InputAction::ToggleMode => {
                    let new_mode = match state.mode {
                        AgentMode::Agent => AgentMode::Plan,
                        AgentMode::Plan => AgentMode::Agent,
                    };
                    state.mode = new_mode.clone();
                    if let Some(thread_id) = state.current_thread_id.clone() {
                        let mode_str = match new_mode {
                            AgentMode::Agent => "agent",
                            AgentMode::Plan => "plan",
                        };
                        wire.send_request(
                            "thread/mode/set",
                            serde_json::json!({ "threadId": thread_id, "mode": mode_str }),
                        )
                        .await?;
                    }
                }
                InputAction::ForceRedraw => {
                    terminal.clear()?;
                }
                InputAction::ApprovalDecision(_)
                | InputAction::ThreadPickerAction(_)
                | InputAction::ModelPickerAction(_)
                | InputAction::CloseOverlay
                | InputAction::None => {}
            }
        }
        CrosstermEvent::Paste(text) => {
            state.input_history_pos = None;
            state.input_text.insert_str(state.input_cursor, &text);
            state.input_cursor += text.len();
        }
        CrosstermEvent::Mouse(mouse) => {
            // Modal overlays own input; wheel should not affect base transcript while active.
            if state.active_overlay.is_some() {
                return Ok(false);
            }

            match mouse.kind {
                MouseEventKind::ScrollUp => {
                    input_router::enter_transcript_browse(state);
                    input_router::scroll_line_up(state);
                }
                MouseEventKind::ScrollDown => {
                    input_router::enter_transcript_browse(state);
                    input_router::scroll_line_down(state);
                }
                _ => {}
            }
        }
        CrosstermEvent::Resize(_w, _h) => {
            // Ratatui redraws at new size automatically on next tick.
        }
        _ => {}
    }
    Ok(false)
}

// ── Wire action helpers ───────────────────────────────────────────────────

fn build_identity(workspace_path: &str) -> serde_json::Value {
    serde_json::json!({
        "channelName": "cli",
        "userId": "local",
        "workspacePath": workspace_path
    })
}

async fn spawn_model_catalog_load(
    wire: &mut WireClient,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
) -> Result<()> {
    let (_, rx) = wire
        .send_request("model/list", serde_json::json!({}))
        .await?;
    let tx = deferred_tx.clone();
    tokio::spawn(async move {
        let result = rx
            .await
            .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
        let _ = tx.send(DeferredResult::ModelCatalogLoaded(result));
    });
    Ok(())
}

fn normalize_command_language(language: &str) -> &'static str {
    if language.eq_ignore_ascii_case("zh")
        || language.eq_ignore_ascii_case("zh-cn")
        || language.eq_ignore_ascii_case("zh_cn")
    {
        "zh"
    } else {
        "en"
    }
}

async fn refresh_command_catalog(
    wire: &mut WireClient,
    state: &mut AppState,
    language: &str,
) -> Result<()> {
    if !wire.capabilities.command_management.unwrap_or(false) {
        state.server_commands.clear();
        state.command_catalog = commands::merge_command_catalog(&state.server_commands);
        return Ok(());
    }

    let result: wire::types::CommandListResult = wire
        .request(
            "command/list",
            serde_json::json!({ "language": normalize_command_language(language) }),
        )
        .await?;
    state.server_commands = result.commands;
    state.command_catalog = commands::merge_command_catalog(&state.server_commands);
    Ok(())
}

async fn create_thread(wire: &mut WireClient, state: &mut AppState) -> Result<()> {
    let ws = &state.workspace_path;
    let params = if let Some(model) = state.pending_model_override.clone() {
        serde_json::json!({
            "identity": build_identity(ws),
            "config": { "model": model }
        })
    } else {
        serde_json::json!({
            "identity": build_identity(ws)
        })
    };

    let result: serde_json::Value = wire.request("thread/start", params).await?;
    if let Some(thread) = result.get("thread") {
        state.current_thread_id = thread
            .get("id")
            .and_then(|v| v.as_str())
            .map(str::to_string);
        state.current_thread_name = thread
            .get("displayName")
            .and_then(|v| v.as_str())
            .map(str::to_string);
        state.current_model_override = thread
            .get("configuration")
            .and_then(|cfg| cfg.get("model"))
            .and_then(|v| v.as_str())
            .map(str::to_string)
            .or_else(|| state.pending_model_override.clone());
        state.pending_model_override = None;
    }
    Ok(())
}

async fn submit_turn(wire: &mut WireClient, state: &mut AppState, text: String) -> Result<()> {
    // Lazy thread creation: materialize on first user input.
    if state.current_thread_id.is_none() {
        create_thread(wire, state).await?;
    }

    let thread_id = match &state.current_thread_id {
        Some(id) => id.clone(),
        None => {
            state.history.push(HistoryEntry::Error {
                message: "Failed to create thread.".to_string(),
            });
            return Ok(());
        }
    };

    if state.turn_status != TurnStatus::Idle {
        state.history.push(HistoryEntry::Error {
            message: "A turn is already in progress. Use Ctrl+C to interrupt it.".to_string(),
        });
        return Ok(());
    }

    state
        .history
        .push(HistoryEntry::UserMessage { text: text.clone() });
    state.at_bottom = true;

    let params = serde_json::json!({
        "threadId": thread_id,
        "input": [{ "type": "text", "text": text }]
    });

    match wire.send_request("turn/start", params).await {
        Ok(_) => {
            state.turn_status = TurnStatus::Running;
        }
        Err(e) => {
            state.history.push(HistoryEntry::Error {
                message: format!("Failed to start turn: {e}"),
            });
            return Err(e);
        }
    }
    Ok(())
}

async fn handle_interrupt(wire: &mut WireClient, state: &mut AppState) -> Result<bool> {
    let now = Instant::now();

    if state.turn_status == TurnStatus::Running || state.turn_status == TurnStatus::WaitingApproval
    {
        // Double Ctrl+C within 1 second exits even while a turn is running.
        if let Some(last) = state.last_interrupt_at {
            if now.duration_since(last) < Duration::from_secs(1) {
                return Ok(true);
            }
        }
        if let Some(thread_id) = &state.current_thread_id.clone() {
            let turn_id = state.current_turn_id.clone().unwrap_or_default();
            let params = serde_json::json!({
                "threadId": thread_id,
                "turnId": turn_id
            });
            let _ = wire.send_request("turn/interrupt", params).await;
        }
        state.last_interrupt_at = Some(now);
        return Ok(false);
    }

    if let Some(last) = state.last_interrupt_at {
        if now.duration_since(last) < Duration::from_secs(1) {
            return Ok(true);
        }
    }
    state.last_interrupt_at = Some(now);
    Ok(false)
}

async fn handle_server_request(
    _wire: &mut WireClient,
    state: &mut AppState,
    msg: wire::types::JsonRpcMessage,
) -> Result<()> {
    let method = msg.method.as_deref().unwrap_or("");
    if method == "item/approval/request" {
        let params = msg.params.as_ref().unwrap_or(&serde_json::Value::Null);
        let approval_type = params
            .get("approvalType")
            .and_then(|v| v.as_str())
            .unwrap_or("shell")
            .to_string();
        let operation = params
            .get("operation")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let target = params
            .get("target")
            .and_then(|v| v.as_str())
            .unwrap_or("")
            .to_string();
        let reason = params
            .get("reason")
            .and_then(|v| v.as_str())
            .map(str::to_string);

        if let Some(id) = msg.id {
            state.pending_approval = Some(ApprovalState {
                request_id: id,
                approval_type,
                operation,
                target,
                reason,
                selected: 0,
            });
            state.active_overlay = Some(OverlayKind::Approval);
            state.turn_status = TurnStatus::WaitingApproval;
        }
    }
    Ok(())
}

fn is_server_request(msg: &wire::types::JsonRpcMessage) -> bool {
    msg.id.is_some() && msg.method.is_some()
}

/// Process a deferred async result that arrived from a spawned task.
fn handle_deferred_result(state: &mut AppState, strings: &Strings, result: DeferredResult) {
    match result {
        DeferredResult::ModelCatalogLoaded(Ok(value)) => {
            let (models, error) = parse_model_catalog(&value);
            if let Some(err) = error {
                state.model_cache = ModelCacheState::Error(err.clone());
                if let Some(picker) = state.model_picker.as_mut() {
                    picker.loading = false;
                    picker.error = Some(err);
                    picker.models.clear();
                }
            } else {
                state.model_cache = ModelCacheState::Ready(models.clone());
                if let Some(picker) = state.model_picker.as_mut() {
                    picker.loading = false;
                    picker.error = None;
                    picker.models = models;
                    if picker.selected >= picker.models.len() {
                        picker.selected = 0;
                    }
                }
            }
        }
        DeferredResult::ModelCatalogLoaded(Err(e)) => {
            let msg = format!("Failed to load models: {e}");
            state.model_cache = ModelCacheState::Error(msg.clone());
            if let Some(picker) = state.model_picker.as_mut() {
                picker.loading = false;
                picker.error = Some(msg);
                picker.models.clear();
            }
        }
        DeferredResult::ThreadListLoaded(Ok(value)) => {
            let threads = parse_thread_list(&value);
            if let Some(picker) = state.thread_picker.as_mut() {
                picker.threads = threads;
                picker.loading = false;
            }
        }
        DeferredResult::ThreadListLoaded(Err(e)) => {
            if let Some(picker) = state.thread_picker.as_mut() {
                picker.loading = false;
                picker.error = Some(format!("Failed to load sessions: {e}"));
            }
        }
        DeferredResult::ThreadHistoryLoaded(Ok(data)) => {
            replay_thread_history(state, &data);
            let label = state
                .current_thread_name
                .as_deref()
                .or(state.current_thread_id.as_deref())
                .unwrap_or("?");
            let message = format!("{} {label}", strings.session_loaded_prefix);
            let is_duplicate = matches!(
                state.history.last(),
                Some(HistoryEntry::SystemInfo { message: existing }) if existing == &message
            );
            if !is_duplicate {
                state.history.push(HistoryEntry::SystemInfo { message });
            }
        }
        DeferredResult::ThreadHistoryLoaded(Err(e)) => {
            state.history.push(HistoryEntry::Error {
                message: format!("Failed to load thread history: {e}"),
            });
        }
    }
}

/// Dispatch a ThreadPickerAction returned from the input router.
async fn handle_thread_picker_action(
    wire: &mut WireClient,
    state: &mut AppState,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    action: InputAction,
) -> Result<()> {
    match action {
        InputAction::ThreadPickerAction(ThreadPickerOp::Close) => {
            state.active_overlay = None;
            state.thread_picker = None;
        }
        InputAction::ThreadPickerAction(ThreadPickerOp::Resume) => {
            let selected = state
                .thread_picker
                .as_ref()
                .and_then(|p| p.threads.get(p.selected))
                .map(|t| (t.id.clone(), t.display_name.clone()));
            if let Some((id, display_name)) = selected {
                state.active_overlay = None;
                state.thread_picker = None;
                state.history.clear();
                state.plan = None;
                state.subagent_entries.clear();
                state.streaming.clear();
                state.token_tracker.reset();
                wire.send_request("thread/resume", serde_json::json!({ "threadId": id }))
                    .await?;
                state.current_thread_id = Some(id.clone());
                state.current_thread_name = display_name;
                // Fire async thread/read; result handled via deferred channel.
                let (_, rx) = wire
                    .send_request(
                        "thread/read",
                        serde_json::json!({ "threadId": id, "includeTurns": true }),
                    )
                    .await?;
                let tx = deferred_tx.clone();
                tokio::spawn(async move {
                    let result = rx
                        .await
                        .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
                    let _ = tx.send(DeferredResult::ThreadHistoryLoaded(result));
                });
            }
        }
        InputAction::ThreadPickerAction(ThreadPickerOp::Archive) => {
            let thread_id = state
                .thread_picker
                .as_ref()
                .and_then(|p| p.threads.get(p.selected))
                .map(|t| t.id.clone());
            if let Some(id) = thread_id {
                wire.send_request("thread/archive", serde_json::json!({ "threadId": id }))
                    .await?;
                // Remove from local list immediately for instant feedback.
                if let Some(picker) = state.thread_picker.as_mut() {
                    if !picker.threads.is_empty() {
                        picker.threads.remove(picker.selected);
                        if picker.selected >= picker.threads.len() && picker.selected > 0 {
                            picker.selected -= 1;
                        }
                    }
                }
            }
        }
        InputAction::ThreadPickerAction(ThreadPickerOp::Delete) => {
            let thread_id = state
                .thread_picker
                .as_ref()
                .and_then(|p| p.threads.get(p.selected))
                .map(|t| t.id.clone());
            if let Some(id) = thread_id {
                wire.send_request("thread/delete", serde_json::json!({ "threadId": id }))
                    .await?;
                if let Some(picker) = state.thread_picker.as_mut() {
                    if !picker.threads.is_empty() {
                        picker.threads.remove(picker.selected);
                        if picker.selected >= picker.threads.len() && picker.selected > 0 {
                            picker.selected -= 1;
                        }
                    }
                }
            }
        }
        _ => {}
    }
    Ok(())
}

async fn handle_model_picker_action(
    wire: &mut WireClient,
    state: &mut AppState,
    action: InputAction,
) -> Result<()> {
    match action {
        InputAction::ModelPickerAction(ModelPickerOp::Close) => {
            state.active_overlay = None;
            state.model_picker = None;
        }
        InputAction::ModelPickerAction(ModelPickerOp::Apply) => {
            let selected_model = state
                .model_picker
                .as_ref()
                .and_then(|p| p.models.get(p.selected))
                .cloned();
            if let Some(model_label) = selected_model {
                let model_override = if model_label.eq_ignore_ascii_case("default") {
                    None
                } else {
                    Some(model_label)
                };
                if let Err(e) = apply_model_override(wire, state, model_override).await {
                    state.history.push(HistoryEntry::Error { message: e });
                }
            }
            state.active_overlay = None;
            state.model_picker = None;
        }
        _ => {}
    }
    Ok(())
}

async fn apply_model_override(
    wire: &mut WireClient,
    state: &mut AppState,
    model: Option<String>,
) -> Result<(), String> {
    let workspace_update = wire
        .request::<serde_json::Value>(
            "workspace/config/update",
            serde_json::json!({ "model": model.clone() }),
        )
        .await
        .map_err(|e| format!("Failed to update workspace model: {e}"))?;
    state.workspace_model = workspace_update
        .get("model")
        .and_then(|v| v.as_str())
        .map(str::to_string);

    if let Some(thread_id) = state.current_thread_id.clone() {
        let read = wire
            .request::<serde_json::Value>(
                "thread/read",
                serde_json::json!({ "threadId": thread_id, "includeTurns": false }),
            )
            .await
            .map_err(|e| {
                state.current_model_override = model.clone();
                state.pending_model_override = None;
                format!("Workspace model updated, but failed to read thread config: {e}")
            })?;

        let mut config = read
            .get("thread")
            .and_then(|t| t.get("configuration"))
            .cloned()
            .unwrap_or_else(|| serde_json::json!({}));
        if !config.is_object() {
            config = serde_json::json!({});
        }

        if let Some(cfg_obj) = config.as_object_mut() {
            let existing_model_key = cfg_obj
                .keys()
                .find(|k| k.eq_ignore_ascii_case("model"))
                .cloned();
            if let Some(next) = model.clone() {
                let key = existing_model_key.unwrap_or_else(|| "model".to_string());
                cfg_obj.insert(key, serde_json::Value::String(next));
            } else if let Some(key) = existing_model_key {
                cfg_obj.remove(&key);
            }
        }

        wire.send_request(
            "thread/config/update",
            serde_json::json!({ "threadId": thread_id, "config": config }),
        )
        .await
        .map_err(|e| {
            state.current_model_override = model.clone();
            state.pending_model_override = None;
            format!("Workspace model updated, but failed to apply thread override: {e}")
        })?;

        state.current_model_override = model;
        state.pending_model_override = None;
    } else {
        state.pending_model_override = model;
        state.current_model_override = state.pending_model_override.clone();
    }
    Ok(())
}

/// Parse the thread/list response into a Vec of ThreadEntry.
fn parse_thread_list(result: &serde_json::Value) -> Vec<ThreadEntry> {
    result
        .get("data")
        .and_then(|v| v.as_array())
        .map(|arr| {
            arr.iter()
                .filter_map(|t| {
                    Some(ThreadEntry {
                        id: t.get("id")?.as_str()?.to_string(),
                        display_name: t
                            .get("displayName")
                            .and_then(|v| v.as_str())
                            .map(str::to_string),
                        status: t
                            .get("status")
                            .and_then(|v| v.as_str())
                            .unwrap_or("unknown")
                            .to_string(),
                        origin_channel: t
                            .get("originChannel")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_string(),
                        last_active_at: t
                            .get("lastActiveAt")
                            .and_then(|v| v.as_str())
                            .unwrap_or("")
                            .to_string(),
                    })
                })
                .collect()
        })
        .unwrap_or_default()
}

fn parse_model_catalog(result: &serde_json::Value) -> (Vec<String>, Option<String>) {
    let success = result
        .get("success")
        .and_then(|v| v.as_bool())
        .unwrap_or(false);
    if !success {
        let message = result
            .get("errorMessage")
            .and_then(|v| v.as_str())
            .or_else(|| result.get("errorCode").and_then(|v| v.as_str()))
            .unwrap_or("Model catalog request failed.")
            .to_string();
        return (vec![], Some(message));
    }

    let mut models: Vec<String> = result
        .get("models")
        .and_then(|v| v.as_array())
        .map(|arr| {
            arr.iter()
                .filter_map(|item| item.get("id").and_then(|v| v.as_str()))
                .map(|id| id.trim().to_string())
                .filter(|id| !id.is_empty())
                .collect::<Vec<String>>()
        })
        .unwrap_or_default();

    models.sort_by_key(|a| a.to_ascii_lowercase());
    models.dedup_by(|a, b| a.eq_ignore_ascii_case(b));
    (models, None)
}

fn stringify_history_args(value: &serde_json::Value) -> String {
    if let Some(s) = value.as_str() {
        s.to_string()
    } else {
        serde_json::to_string_pretty(value).unwrap_or_default()
    }
}

fn plugin_function_history_result(payload: &serde_json::Value) -> Option<String> {
    if let Some(items) = payload.get("contentItems").and_then(|v| v.as_array()) {
        let mut parts = Vec::new();
        for item in items {
            match item.get("type").and_then(|v| v.as_str()).unwrap_or("text") {
                "text" => {
                    if let Some(text) = item.get("text").and_then(|v| v.as_str()) {
                        if !text.is_empty() {
                            parts.push(text.to_string());
                        }
                    }
                }
                "image" => {
                    let media_type = item
                        .get("mediaType")
                        .and_then(|v| v.as_str())
                        .unwrap_or("image");
                    parts.push(format!("[image: {media_type}]"));
                }
                _ => {}
            }
        }
        if !parts.is_empty() {
            return Some(parts.join("\n"));
        }
    }

    if let Some(structured) = payload.get("structuredResult") {
        if !structured.is_null() {
            return Some(
                serde_json::to_string_pretty(structured).unwrap_or_else(|_| structured.to_string()),
            );
        }
    }

    payload
        .get("errorMessage")
        .and_then(|v| v.as_str())
        .map(str::to_string)
}

/// Parse a `thread/read` response (with `includeTurns: true`) and rebuild
/// `state.history` from the persisted items.
fn replay_thread_history(state: &mut AppState, data: &serde_json::Value) {
    // Sync thread displayName into state so the status bar is accurate.
    if let Some(name) = data
        .get("thread")
        .and_then(|t| t.get("displayName"))
        .and_then(|v| v.as_str())
    {
        state.current_thread_name = Some(name.to_string());
    }
    state.current_model_override = data
        .get("thread")
        .and_then(|t| t.get("configuration"))
        .and_then(|cfg| cfg.get("model"))
        .and_then(|v| v.as_str())
        .map(str::to_string);

    let turns = match data
        .get("thread")
        .and_then(|t| t.get("turns"))
        .and_then(|v| v.as_array())
    {
        Some(t) => t,
        None => return,
    };

    for turn in turns {
        let items = match turn.get("items").and_then(|v| v.as_array()) {
            Some(i) => i,
            None => continue,
        };
        for item in items {
            let item_type = item.get("type").and_then(|v| v.as_str()).unwrap_or("");
            let payload = item
                .get("payload")
                .cloned()
                .unwrap_or(serde_json::Value::Null);

            match item_type {
                "userMessage" => {
                    if let Some(text) = payload.get("text").and_then(|v| v.as_str()) {
                        state.history.push(HistoryEntry::UserMessage {
                            text: text.to_string(),
                        });
                    }
                }
                "agentMessage" => {
                    if let Some(text) = payload.get("text").and_then(|v| v.as_str()) {
                        state.history.push(HistoryEntry::AgentMessage {
                            text: text.to_string(),
                        });
                    }
                }
                "toolCall" => {
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let name = payload
                        .get("toolName")
                        .and_then(|v| v.as_str())
                        .unwrap_or("unknown")
                        .to_string();
                    let args = payload
                        .get("arguments")
                        .map(stringify_history_args)
                        .unwrap_or_default();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    state.history.push(HistoryEntry::ToolCall {
                        call_id,
                        name,
                        args,
                        result: None,
                        success,
                        duration: None,
                    });
                }
                "pluginFunctionCall" => {
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let name = payload
                        .get("functionName")
                        .and_then(|v| v.as_str())
                        .unwrap_or("unknown")
                        .to_string();
                    let args = payload
                        .get("arguments")
                        .map(stringify_history_args)
                        .unwrap_or_default();
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    state.history.push(HistoryEntry::ToolCall {
                        call_id,
                        name,
                        args,
                        result: plugin_function_history_result(&payload),
                        success,
                        duration: None,
                    });
                }
                "toolResult" => {
                    let call_id = payload
                        .get("callId")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let result_text = payload
                        .get("result")
                        .and_then(|v| v.as_str())
                        .map(str::to_string);
                    let success = payload
                        .get("success")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
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
                "error" => {
                    let msg = payload
                        .get("message")
                        .or_else(|| payload.get("text"))
                        .and_then(|v| v.as_str())
                        .unwrap_or("Unknown error")
                        .to_string();
                    state.history.push(HistoryEntry::Error { message: msg });
                }
                _ => {}
            }
        }
    }
}

async fn handle_slash_command(
    wire: &mut WireClient,
    state: &mut AppState,
    strings: &Strings,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    cmd: ParsedSlashCommand,
) -> Result<bool> {
    if let Some(local_cmd) = commands::to_local_command(&cmd) {
        return handle_local_slash_command(wire, state, strings, deferred_tx, local_cmd).await;
    }

    if state
        .server_commands
        .iter()
        .any(|server_cmd| server_cmd.name.eq_ignore_ascii_case(&cmd.name))
    {
        execute_server_command(wire, state, &cmd).await?;
    } else {
        let name = cmd.name.trim_start_matches('/');
        state.history.push(HistoryEntry::Error {
            message: format!("Unknown command: /{name}. Type /help for available commands."),
        });
    }
    Ok(false)
}

async fn handle_local_slash_command(
    wire: &mut WireClient,
    state: &mut AppState,
    strings: &Strings,
    deferred_tx: &tokio_mpsc::UnboundedSender<DeferredResult>,
    cmd: LocalSlashCommand,
) -> Result<bool> {
    match cmd {
        LocalSlashCommand::Quit => return Ok(true),
        LocalSlashCommand::Clear => {
            state.history.clear();
            state.plan = None;
            state.subagent_entries.clear();
        }
        LocalSlashCommand::Plan => {
            if let Some(thread_id) = state.current_thread_id.clone() {
                wire.send_request(
                    "thread/mode/set",
                    serde_json::json!({ "threadId": thread_id, "mode": "plan" }),
                )
                .await?;
                state.mode = AgentMode::Plan;
            }
        }
        LocalSlashCommand::Agent => {
            if let Some(thread_id) = state.current_thread_id.clone() {
                wire.send_request(
                    "thread/mode/set",
                    serde_json::json!({ "threadId": thread_id, "mode": "agent" }),
                )
                .await?;
                state.mode = AgentMode::Agent;
            }
        }
        LocalSlashCommand::Model { model_name } => {
            if !wire.capabilities.model_catalog_management.unwrap_or(false)
                || !wire
                    .capabilities
                    .workspace_config_management
                    .unwrap_or(false)
            {
                state.history.push(HistoryEntry::Error {
                    message: strings.feature_unavailable.to_string(),
                });
                return Ok(false);
            }

            if let Some(model_input) = model_name {
                let model = model_input.trim();
                if model.is_empty() {
                    state.history.push(HistoryEntry::Error {
                        message: strings.model_usage.to_string(),
                    });
                    return Ok(false);
                }
                let next = if model.eq_ignore_ascii_case("default") {
                    None
                } else {
                    Some(model.to_string())
                };
                match apply_model_override(wire, state, next.clone()).await {
                    Ok(()) => {
                        let message = match next {
                            Some(m) => strings.model_updated_to.replace("{}", &m),
                            None => strings.model_updated_default.to_string(),
                        };
                        state.history.push(HistoryEntry::SystemInfo { message });
                    }
                    Err(e) => {
                        state.history.push(HistoryEntry::Error { message: e });
                    }
                }
                return Ok(false);
            }

            let (loading, models, error) = match &state.model_cache {
                ModelCacheState::Loading => (true, vec!["Default".to_string()], None),
                ModelCacheState::Ready(cached) => {
                    let mut all = vec!["Default".to_string()];
                    all.extend(cached.iter().cloned());
                    (false, all, None)
                }
                ModelCacheState::Error(err) => (false, vec![], Some(err.clone())),
                ModelCacheState::Idle => (true, vec!["Default".to_string()], None),
            };
            state.model_picker = Some(ModelPickerState {
                models,
                selected: 0,
                loading,
                error,
            });
            state.active_overlay = Some(OverlayKind::ModelPicker);
        }
        LocalSlashCommand::Help => {
            state.active_overlay = Some(OverlayKind::Help);
        }
        LocalSlashCommand::Sessions => {
            if !wire.capabilities.thread_management.unwrap_or(false) {
                state.history.push(HistoryEntry::Error {
                    message: strings.feature_unavailable.to_string(),
                });
                return Ok(false);
            }

            state.thread_picker = Some(ThreadPickerState {
                threads: vec![],
                selected: 0,
                loading: true,
                error: None,
            });
            state.active_overlay = Some(OverlayKind::ThreadPicker);

            let identity = build_identity(&state.workspace_path);
            let (_, rx) = wire
                .send_request("thread/list", serde_json::json!({ "identity": identity }))
                .await?;
            let tx = deferred_tx.clone();
            tokio::spawn(async move {
                let result = rx
                    .await
                    .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
                let _ = tx.send(DeferredResult::ThreadListLoaded(result));
            });
        }
        LocalSlashCommand::Load { thread_id } => {
            if thread_id.is_empty() {
                state.history.push(HistoryEntry::Error {
                    message: "Usage: /load <thread-id>".to_string(),
                });
                return Ok(false);
            }
            let id = thread_id.clone();
            state.history.clear();
            state.plan = None;
            state.subagent_entries.clear();
            state.streaming.clear();
            state.token_tracker.reset();
            wire.send_request("thread/resume", serde_json::json!({ "threadId": id }))
                .await?;
            state.current_thread_id = Some(id.clone());

            let (_, rx) = wire
                .send_request(
                    "thread/read",
                    serde_json::json!({ "threadId": id, "includeTurns": true }),
                )
                .await?;
            let tx = deferred_tx.clone();
            tokio::spawn(async move {
                let result = rx
                    .await
                    .unwrap_or_else(|_| Err(anyhow::anyhow!("response dropped")));
                let _ = tx.send(DeferredResult::ThreadHistoryLoaded(result));
            });
        }
    }
    Ok(false)
}

async fn execute_server_command(
    wire: &mut WireClient,
    state: &mut AppState,
    cmd: &ParsedSlashCommand,
) -> Result<()> {
    if state.current_thread_id.is_none() {
        create_thread(wire, state).await?;
    }

    let thread_id = match &state.current_thread_id {
        Some(id) => id.clone(),
        None => {
            state.history.push(HistoryEntry::Error {
                message: "Failed to create thread.".to_string(),
            });
            return Ok(());
        }
    };

    let arguments = if cmd.arguments.is_empty() {
        serde_json::Value::Null
    } else {
        serde_json::json!(cmd.arguments)
    };
    let result: wire::types::CommandExecuteResult = wire
        .request(
            "command/execute",
            serde_json::json!({
                "threadId": thread_id,
                "command": cmd.name,
                "arguments": arguments
            }),
        )
        .await?;

    if let Some(message) = result.message.clone().filter(|m| !m.trim().is_empty()) {
        let _is_markdown = result.is_markdown;
        state.history.push(HistoryEntry::SystemInfo { message });
    }

    if result.session_reset.unwrap_or(false) {
        state.history.clear();
        state.plan = None;
        state.subagent_entries.clear();
        state.streaming.clear();
        state.token_tracker.reset();
        state.current_turn_id = None;
        state.current_model_override = None;
        state.pending_model_override = None;
        if let Some(thread) = result.thread {
            state.current_thread_id = Some(thread.id);
            state.current_thread_name = thread.display_name;
        } else {
            state.current_thread_id = None;
            state.current_thread_name = None;
        }
    }

    if result.handled {
        if let Some(expanded_prompt) = result.expanded_prompt.filter(|p| !p.trim().is_empty()) {
            state.streaming.clear();
            submit_turn(wire, state, expanded_prompt).await?;
        }
    } else {
        state.history.push(HistoryEntry::Error {
            message: format!("Command not handled: {}", cmd.name),
        });
    }

    Ok(())
}

fn expire_notifications(state: &mut AppState) {
    let now_ms = chrono::Utc::now().timestamp_millis();
    state.notifications.retain(|n| n.dismiss_at_ms > now_ms);
}

// ── WebSocket Reconnection ───────────────────────────────────────────────

/// Reconnect to a WebSocket AppServer with exponential backoff (1s-30s) + jitter.
/// Keeps rendering the UI during the wait so the user sees status updates.
/// Returns a new WireClient on success, or Err if the user presses Ctrl+C.
#[cfg(feature = "websocket")]
async fn reconnect_ws(
    url: &str,
    state: &mut AppState,
    terminal: &mut Term,
    theme: &Theme,
    strings: &Strings,
    event_stream: &mut EventStream,
) -> Result<WireClient> {
    let mut delay = Duration::from_secs(1);
    let max_delay = Duration::from_secs(30);
    let mut attempt = 0u32;

    loop {
        attempt += 1;
        state.connected = false;
        tracing::info!("Reconnect attempt {attempt}, delay: {delay:?}");

        // Simple jitter based on system time to avoid thundering herd.
        let nanos = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default()
            .subsec_nanos();
        let jitter = Duration::from_millis((nanos % 500) as u64);
        let deadline = Instant::now() + delay + jitter;
        let mut tick = time::interval(Duration::from_millis(16));
        tick.set_missed_tick_behavior(time::MissedTickBehavior::Skip);

        loop {
            if Instant::now() >= deadline {
                break;
            }
            tokio::select! {
                Some(evt_result) = event_stream.next() => {
                    if let Ok(CrosstermEvent::Key(key)) = evt_result {
                        if key.kind == KeyEventKind::Press
                            && key.code == crossterm::event::KeyCode::Char('c')
                            && key.modifiers.contains(crossterm::event::KeyModifiers::CONTROL)
                        {
                            anyhow::bail!("User cancelled reconnection");
                        }
                    }
                }
                _ = tick.tick() => {
                    state.tick_count = state.tick_count.wrapping_add(1);
                    expire_notifications(state);
                    draw(terminal, state, theme, strings)?;
                }
            }
        }

        match Transport::connect_ws(url).await {
            Ok(transport) => {
                let mut new_wire = WireClient::spawn(transport);
                match new_wire.initialize().await {
                    Ok(()) => {
                        tracing::info!("Reconnected after {attempt} attempt(s)");
                        return Ok(new_wire);
                    }
                    Err(e) => {
                        tracing::warn!("Reconnect handshake failed: {e}");
                    }
                }
            }
            Err(e) => {
                tracing::warn!("Reconnect attempt {attempt} failed: {e}");
            }
        }

        delay = (delay * 2).min(max_delay);
    }
}

// ── Draw ──────────────────────────────────────────────────────────────────

fn draw_welcome(
    terminal: &mut Term,
    state: &AppState,
    theme: &Theme,
    strings: &Strings,
    version: &str,
) -> Result<()> {
    terminal::prepare_frame(terminal);
    terminal.draw(|frame| {
        let area = frame.area();
        frame.render_widget(
            WelcomeScreen::new(
                version,
                &state.workspace_path,
                state
                    .current_model_override
                    .as_deref()
                    .or(state.workspace_model.as_deref())
                    .or(Some(strings.model_default_label)),
                state.connected,
                state.tick_count,
                theme,
                strings,
            ),
            area,
        );
    })?;
    Ok(())
}

fn draw(terminal: &mut Term, state: &AppState, theme: &Theme, strings: &Strings) -> Result<()> {
    terminal::prepare_frame(terminal);
    terminal.draw(|frame| {
        let area = frame.area();
        let has_active_turn = state.turn_status == TurnStatus::Running
            || state.turn_status == TurnStatus::WaitingApproval;
        let show_status_zone = has_active_turn || state.system_status.is_some();
        let has_pending = !state.pending_input.is_empty();
        let input_h = InputEditor::preferred_height(state, area.width);
        let status_h = StatusIndicator::preferred_height(state);
        let zones = layout::compute(area, show_status_zone, has_pending, input_h, status_h);

        // ChatView: pass actual available width for correct markdown wrap.
        let chat_width = zones.chat_view.width;

        // ── Base UI ───────────────────────────────────────────────────────
        frame.render_widget(
            ChatView::new(state, theme, strings).with_width(chat_width),
            zones.chat_view,
        );

        if let Some(si_area) = zones.status_indicator {
            frame.render_widget(StatusIndicator::new(state, theme, strings), si_area);
        }

        // Pending input preview (between StatusIndicator and InputEditor).
        if let Some(pp_area) = zones.pending_preview {
            if let Some(queued) = state.pending_input.first() {
                use ratatui::{
                    text::{Line, Span},
                    widgets::{Paragraph, Widget},
                };
                let preview = format!("  ┄ {}: \"{queued}\"", strings.pending_queued_prefix);
                Paragraph::new(Line::from(Span::styled(preview, theme.dim)))
                    .render(pp_area, frame.buffer_mut());
            }
        }

        frame.render_widget(InputEditor::new(state, theme, strings), zones.input_editor);

        if let Some(footer_area) = zones.footer {
            frame.render_widget(FooterLine::new(state, theme, strings), footer_area);
        }

        // Keep the caret visible whenever the input editor has focus and no overlay is active.
        if state.focus == crate::app::state::FocusTarget::InputEditor
            && state.active_overlay.is_none()
        {
            // 2 = gutter width ("❯ " / "✎ ")
            let inner_w = zones.input_editor.width.saturating_sub(2);
            let (row, col) =
                ui::input_editor::offset_to_2d(&state.input_text, state.input_cursor, inner_w);
            let cursor_x = zones.input_editor.x + 2 + col.min(inner_w.saturating_sub(1));
            let cursor_y =
                zones.input_editor.y + row.min(zones.input_editor.height.saturating_sub(1));
            frame.set_cursor_position((cursor_x, cursor_y));
        }

        // ── Command completion popup (above input) ─────────────────────
        if let Some(popup_state) = &state.command_popup {
            let popup_area = CommandPopup::popup_area(zones.input_editor, popup_state.items.len());
            frame.render_widget(CommandPopup::new(popup_state, theme), popup_area);
        }

        // ── Notification toast (non-modal, top-right) ─────────────────────
        if !state.notifications.is_empty() {
            frame.render_widget(NotificationToast::new(state, theme, strings), area);
        }

        // ── Modal overlays (render last, on top) ──────────────────────────
        match &state.active_overlay {
            Some(OverlayKind::Approval) => {
                if let Some(approval) = &state.pending_approval {
                    frame.render_widget(ApprovalOverlay::new(approval, theme, strings), area);
                }
            }
            Some(OverlayKind::ThreadPicker) => {
                if let Some(picker) = &state.thread_picker {
                    frame.render_widget(ThreadPicker::new(picker, theme, strings), area);
                }
            }
            Some(OverlayKind::ModelPicker) => {
                if let Some(picker) = &state.model_picker {
                    frame.render_widget(
                        ModelPicker::new(picker, state.tick_count, theme, strings),
                        area,
                    );
                }
            }
            Some(OverlayKind::Help) => {
                frame.render_widget(
                    HelpOverlay::new(theme, strings, &state.command_catalog),
                    area,
                );
            }
            None => {}
        }
    })?;
    Ok(())
}
