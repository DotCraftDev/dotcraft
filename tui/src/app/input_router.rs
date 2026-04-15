// Maps terminal key/paste/resize events to AppState mutations.
// Phase 2: Shift+Enter newline, Tab reasoning toggle, PageUp/Down, Home/End.
// Phase 3: Approval overlay key handling, ApprovalDecision action.
// Phase 4: ThreadPicker overlay, HelpOverlay, F1/? global binding.

use crate::app::state::{AppState, FocusTarget, TurnStatus};

/// Actions available in the ThreadPicker overlay.
#[derive(Debug)]
pub enum ThreadPickerOp {
    Resume,
    Archive,
    Delete,
    Close,
}

#[derive(Debug)]
pub enum ModelPickerOp {
    Apply,
    Close,
}

/// Returned by key handlers to communicate the required action to the event loop.
#[derive(Debug)]
pub enum InputAction {
    /// Submit the current input text as a new turn.
    SubmitTurn(String),
    /// Send a turn interrupt request (Ctrl+C). Contributes to double-press quit within 1s.
    Interrupt,
    /// Send a turn interrupt request (Esc). Does not contribute to double-press quit.
    SoftInterrupt,
    /// Quit the TUI.
    Quit,
    /// User chose a decision for an approval overlay.
    ApprovalDecision(String),
    /// User performed an action in the thread-picker overlay.
    ThreadPickerAction(ThreadPickerOp),
    /// User performed an action in the model-picker overlay.
    ModelPickerAction(ModelPickerOp),
    /// Dismiss the current non-approval overlay (HelpOverlay, etc.).
    CloseOverlay,
    /// Open the HelpOverlay.
    OpenHelp,
    /// Toggle Agent/Plan mode (Shift+Tab).
    ToggleMode,
    /// Force a full terminal redraw (Ctrl+L).
    ForceRedraw,
    /// No action needed beyond the AppState mutation already applied.
    None,
}

/// Process a crossterm key event and return the action to perform.
pub fn handle_key(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    // Global bindings (regardless of focus).
    if key.modifiers == KeyModifiers::CONTROL {
        match key.code {
            KeyCode::Char('c') => return InputAction::Interrupt,
            KeyCode::Char('d') => return InputAction::Quit,
            KeyCode::Char('l') => return InputAction::ForceRedraw,
            _ => {}
        }
    }

    // Global: Esc interrupts a running or approval-waiting turn.
    if key.code == KeyCode::Esc
        && (state.turn_status == TurnStatus::Running
            || state.turn_status == TurnStatus::WaitingApproval)
    {
        return InputAction::SoftInterrupt;
    }

    // Shift+Tab (BackTab) toggles Agent/Plan mode from any focus.
    if key.code == KeyCode::BackTab {
        return InputAction::ToggleMode;
    }

    // Global: F1 opens help regardless of focus.
    if key.code == KeyCode::F(1) {
        return InputAction::OpenHelp;
    }

    match state.focus {
        FocusTarget::InputEditor => handle_input_editor(state, key),
        FocusTarget::ChatView => handle_chat_view(state, key),
    }
}

fn handle_input_editor(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    // ── Command popup interception ──────────────────────────────────────
    if state.command_popup.is_some() {
        match key.code {
            KeyCode::Tab | KeyCode::Enter if key.modifiers == KeyModifiers::NONE => {
                if let Some(popup) = state.command_popup.take() {
                    if let Some((cmd, _)) = popup.items.get(popup.selected) {
                        state.input_text = format!("{cmd} ");
                        state.input_cursor = state.input_text.len();
                    }
                }
                return InputAction::None;
            }
            KeyCode::Up => {
                if let Some(popup) = state.command_popup.as_mut() {
                    if popup.selected > 0 {
                        popup.selected -= 1;
                    }
                }
                return InputAction::None;
            }
            KeyCode::Down => {
                if let Some(popup) = state.command_popup.as_mut() {
                    if popup.selected + 1 < popup.items.len() {
                        popup.selected += 1;
                    }
                }
                return InputAction::None;
            }
            KeyCode::Esc => {
                state.command_popup = None;
                return InputAction::None;
            }
            _ => {
                // Fall through to normal editing; popup will be updated after.
                state.command_popup = None;
            }
        }
    }

    // ── Normal input handling ────────────────────────────────────────────
    let action = match key.code {
        // Ctrl+V → paste from system clipboard
        KeyCode::Char('v') if key.modifiers == KeyModifiers::CONTROL => {
            if let Ok(text) = crate::clipboard::read_text() {
                state.input_text.insert_str(state.input_cursor, &text);
                state.input_cursor += text.len();
            }
            InputAction::None
        }

        // Enter (no shift) → submit
        KeyCode::Enter if key.modifiers == KeyModifiers::NONE => {
            let text = std::mem::take(&mut state.input_text);
            state.input_cursor = 0;
            state.input_history_pos = None;
            state.command_popup = None;
            if !text.is_empty() {
                state.input_history.push(text.clone());
            }
            InputAction::SubmitTurn(text)
        }

        // Shift+Enter → insert newline at cursor
        KeyCode::Enter if key.modifiers == KeyModifiers::SHIFT => {
            state.input_text.insert(state.input_cursor, '\n');
            state.input_cursor += 1;
            InputAction::None
        }

        KeyCode::Backspace => {
            if state.input_cursor > 0 {
                let before = &state.input_text[..state.input_cursor];
                let char_start = before
                    .char_indices()
                    .next_back()
                    .map(|(i, _)| i)
                    .unwrap_or(0);
                state.input_text.remove(char_start);
                state.input_cursor = char_start;
            }
            InputAction::None
        }

        KeyCode::Delete => {
            if state.input_cursor < state.input_text.len() {
                state.input_text.remove(state.input_cursor);
            }
            InputAction::None
        }

        KeyCode::Left => {
            if state.input_cursor > 0 {
                let before = &state.input_text[..state.input_cursor];
                if let Some((i, _)) = before.char_indices().next_back() {
                    state.input_cursor = i;
                }
            }
            InputAction::None
        }

        KeyCode::Right => {
            if state.input_cursor < state.input_text.len() {
                let c = state.input_text[state.input_cursor..]
                    .chars()
                    .next()
                    .unwrap();
                state.input_cursor += c.len_utf8();
            }
            InputAction::None
        }

        KeyCode::Char('a') if key.modifiers == KeyModifiers::CONTROL => {
            let before = &state.input_text[..state.input_cursor];
            let line_start = before.rfind('\n').map(|i| i + 1).unwrap_or(0);
            state.input_cursor = line_start;
            InputAction::None
        }

        KeyCode::Char('e') if key.modifiers == KeyModifiers::CONTROL => {
            let after = &state.input_text[state.input_cursor..];
            let line_end = after
                .find('\n')
                .map(|i| state.input_cursor + i)
                .unwrap_or(state.input_text.len());
            state.input_cursor = line_end;
            InputAction::None
        }

        // Page keys enter transcript browsing directly without requiring Esc first.
        KeyCode::PageUp => {
            state.focus = FocusTarget::ChatView;
            scroll_page_up(state);
            InputAction::None
        }
        KeyCode::PageDown => {
            state.focus = FocusTarget::ChatView;
            scroll_page_down(state);
            InputAction::None
        }
        KeyCode::Home => {
            state.focus = FocusTarget::ChatView;
            scroll_home(state);
            InputAction::None
        }
        KeyCode::End => {
            state.focus = FocusTarget::ChatView;
            scroll_end(state);
            InputAction::None
        }

        // Up → cycle backward through input history
        KeyCode::Up => {
            if state.input_text.is_empty() {
                let hist_len = state.input_history.len();
                if hist_len > 0 {
                    let pos = match state.input_history_pos {
                        None => hist_len - 1,
                        Some(p) if p > 0 => p - 1,
                        Some(p) => p,
                    };
                    state.input_history_pos = Some(pos);
                    state.input_text = state.input_history[pos].clone();
                    state.input_cursor = state.input_text.len();
                }
            }
            InputAction::None
        }

        // Down → cycle forward through input history
        KeyCode::Down => {
            if state.input_text.is_empty() {
                match state.input_history_pos {
                    None => {}
                    Some(p) if p + 1 < state.input_history.len() => {
                        let pos = p + 1;
                        state.input_history_pos = Some(pos);
                        state.input_text = state.input_history[pos].clone();
                        state.input_cursor = state.input_text.len();
                    }
                    Some(_) => {
                        state.input_history_pos = None;
                        state.input_text.clear();
                        state.input_cursor = 0;
                    }
                }
            }
            InputAction::None
        }

        KeyCode::Tab => {
            // Open command popup if input starts with '/'
            if state.input_text.starts_with('/') {
                let filtered = crate::ui::overlays::command_popup::filter_commands(
                    &state.input_text,
                    &state.command_catalog,
                );
                if !filtered.is_empty() {
                    state.command_popup = Some(crate::app::state::CommandPopupState {
                        items: filtered,
                        selected: 0,
                    });
                }
            } else if !state.input_text.is_empty() && state.turn_status != TurnStatus::Idle {
                // Queue follow-up text while a turn is running; drained on turn completion.
                let text = std::mem::take(&mut state.input_text);
                state.input_cursor = 0;
                state.command_popup = None;
                state.pending_input.push(text);
            }
            InputAction::None
        }

        KeyCode::Char(c) => {
            state.input_text.insert(state.input_cursor, c);
            state.input_cursor += c.len_utf8();
            InputAction::None
        }

        KeyCode::Esc => {
            state.focus = FocusTarget::ChatView;
            InputAction::None
        }

        _ => InputAction::None,
    };

    // Auto-show or update command popup whenever input starts with '/'.
    if state.input_text.starts_with('/') {
        let filtered = crate::ui::overlays::command_popup::filter_commands(
            &state.input_text,
            &state.command_catalog,
        );
        if filtered.is_empty() {
            state.command_popup = None;
        } else {
            let sel = state
                .command_popup
                .as_ref()
                .map(|p| p.selected.min(filtered.len().saturating_sub(1)))
                .unwrap_or(0);
            state.command_popup = Some(crate::app::state::CommandPopupState {
                items: filtered,
                selected: sel,
            });
        }
    } else {
        state.command_popup = None;
    }

    action
}

fn handle_chat_view(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::{KeyCode, KeyModifiers};

    match key.code {
        KeyCode::Up => {
            scroll_line_up(state);
            InputAction::None
        }

        KeyCode::Down => {
            scroll_line_down(state);
            InputAction::None
        }

        KeyCode::PageUp => {
            scroll_page_up(state);
            InputAction::None
        }

        KeyCode::PageDown => {
            scroll_page_down(state);
            InputAction::None
        }

        // Home → jump to top of chat history
        KeyCode::Home => {
            scroll_home(state);
            InputAction::None
        }

        // End → jump to bottom of chat history
        KeyCode::End => {
            scroll_end(state);
            InputAction::None
        }

        // Tab → toggle reasoning visibility
        KeyCode::Tab => {
            state.show_reasoning = !state.show_reasoning;
            InputAction::None
        }

        KeyCode::Char('e') => {
            // Tool call results are always visible in the new design;
            // this key binding is kept as a no-op for backwards compatibility.
            InputAction::None
        }

        // y → yank (copy) last agent message to system clipboard
        KeyCode::Char('y') => {
            let last_msg = state.history.iter().rev().find_map(|entry| {
                if let crate::app::state::HistoryEntry::AgentMessage { text } = entry {
                    Some(text.clone())
                } else {
                    None
                }
            });
            if let Some(text) = last_msg {
                let _ = crate::clipboard::write_text(&text);
            }
            InputAction::None
        }

        KeyCode::Enter | KeyCode::Char('i') => {
            state.focus = FocusTarget::InputEditor;
            InputAction::None
        }
        KeyCode::Char(c)
            if key.modifiers == KeyModifiers::NONE || key.modifiers == KeyModifiers::SHIFT =>
        {
            state.focus = FocusTarget::InputEditor;
            state.input_text.insert(state.input_cursor, c);
            state.input_cursor += c.len_utf8();
            state.input_history_pos = None;
            InputAction::None
        }

        // F1 or '?' opens the help overlay from chat view.
        KeyCode::F(1) | KeyCode::Char('?') => InputAction::OpenHelp,

        _ => InputAction::None,
    }
}

pub fn enter_transcript_browse(state: &mut AppState) {
    state.focus = FocusTarget::ChatView;
}

pub fn scroll_line_up(state: &mut AppState) {
    state.scroll_offset = state.scroll_offset.saturating_add(1);
    state.at_bottom = false;
}

pub fn scroll_line_down(state: &mut AppState) {
    if state.scroll_offset > 0 {
        state.scroll_offset -= 1;
    }
    if state.scroll_offset == 0 {
        state.at_bottom = true;
    }
}

fn scroll_page_up(state: &mut AppState) {
    let page = page_step(state);
    state.scroll_offset = state.scroll_offset.saturating_add(page);
    state.at_bottom = false;
}

fn scroll_page_down(state: &mut AppState) {
    let page = page_step(state);
    if state.scroll_offset >= page {
        state.scroll_offset -= page;
    } else {
        state.scroll_offset = 0;
        state.at_bottom = true;
    }
}

fn scroll_home(state: &mut AppState) {
    state.scroll_offset = usize::MAX / 2; // Large value; will be clamped in ChatView
    state.at_bottom = false;
}

fn scroll_end(state: &mut AppState) {
    state.scroll_offset = 0;
    state.at_bottom = true;
}

fn page_step(state: &AppState) -> usize {
    const MIN_PAGE_STEP: usize = 10;
    state.last_viewport_height.get().max(MIN_PAGE_STEP)
}

/// Handle key events when the ApprovalOverlay is active.
/// Returns `ApprovalDecision(decision_str)` when the user confirms, or `None` for navigation.
pub fn handle_approval_overlay(
    state: &mut AppState,
    key: crossterm::event::KeyEvent,
) -> InputAction {
    use crossterm::event::KeyCode;

    let selected = match state.pending_approval.as_ref() {
        Some(a) => a.selected,
        None => return InputAction::None,
    };
    let decision_count = 5usize;

    match key.code {
        // Navigate up
        KeyCode::Up | KeyCode::Char('k') => {
            if let Some(a) = state.pending_approval.as_mut() {
                if a.selected > 0 {
                    a.selected -= 1;
                }
            }
            InputAction::None
        }

        // Navigate down
        KeyCode::Down | KeyCode::Char('j') => {
            if let Some(a) = state.pending_approval.as_mut() {
                if a.selected + 1 < decision_count {
                    a.selected += 1;
                }
            }
            InputAction::None
        }

        // Confirm current selection
        KeyCode::Enter => {
            let decision = crate::ui::overlays::approval::DECISIONS[selected].to_string();
            InputAction::ApprovalDecision(decision)
        }

        // Direct key shortcuts
        KeyCode::Char('a') => InputAction::ApprovalDecision("accept".to_string()),
        KeyCode::Char('s') => InputAction::ApprovalDecision("acceptForSession".to_string()),
        KeyCode::Char('!') => InputAction::ApprovalDecision("acceptAlways".to_string()),
        KeyCode::Char('d') => InputAction::ApprovalDecision("decline".to_string()),
        KeyCode::Char('c') | KeyCode::Esc => InputAction::ApprovalDecision("cancel".to_string()),

        _ => InputAction::None,
    }
}

/// Handle key events when the ThreadPicker overlay is active.
pub fn handle_thread_picker(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::KeyCode;

    let thread_count = state
        .thread_picker
        .as_ref()
        .map(|p| p.threads.len())
        .unwrap_or(0);

    match key.code {
        KeyCode::Up | KeyCode::Char('k') => {
            if let Some(picker) = state.thread_picker.as_mut() {
                if picker.selected > 0 {
                    picker.selected -= 1;
                }
            }
            InputAction::None
        }

        KeyCode::Down | KeyCode::Char('j') => {
            if let Some(picker) = state.thread_picker.as_mut() {
                if thread_count > 0 && picker.selected + 1 < thread_count {
                    picker.selected += 1;
                }
            }
            InputAction::None
        }

        KeyCode::Enter => InputAction::ThreadPickerAction(ThreadPickerOp::Resume),
        KeyCode::Char('a') => InputAction::ThreadPickerAction(ThreadPickerOp::Archive),
        KeyCode::Char('d') => InputAction::ThreadPickerAction(ThreadPickerOp::Delete),
        KeyCode::Esc | KeyCode::Char('q') => InputAction::ThreadPickerAction(ThreadPickerOp::Close),

        _ => InputAction::None,
    }
}

/// Handle key events when the HelpOverlay is active.
pub fn handle_help_overlay(key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::KeyCode;

    match key.code {
        KeyCode::Esc | KeyCode::Char('q') | KeyCode::Char('?') | KeyCode::F(1) => {
            InputAction::CloseOverlay
        }
        _ => InputAction::None,
    }
}

/// Handle key events when the ModelPicker overlay is active.
pub fn handle_model_picker(state: &mut AppState, key: crossterm::event::KeyEvent) -> InputAction {
    use crossterm::event::KeyCode;

    let model_count = state
        .model_picker
        .as_ref()
        .map(|p| p.models.len())
        .unwrap_or(0);

    match key.code {
        KeyCode::Up | KeyCode::Char('k') => {
            if let Some(picker) = state.model_picker.as_mut() {
                if picker.selected > 0 {
                    picker.selected -= 1;
                }
            }
            InputAction::None
        }
        KeyCode::Down | KeyCode::Char('j') => {
            if let Some(picker) = state.model_picker.as_mut() {
                if model_count > 0 && picker.selected + 1 < model_count {
                    picker.selected += 1;
                }
            }
            InputAction::None
        }
        KeyCode::Enter => InputAction::ModelPickerAction(ModelPickerOp::Apply),
        KeyCode::Esc | KeyCode::Char('q') => InputAction::ModelPickerAction(ModelPickerOp::Close),
        _ => InputAction::None,
    }
}
