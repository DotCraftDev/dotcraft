// FooterLine widget (§8.2 of specs/tui-client.md).
// Single-row contextual footer rendered below InputEditor.
// Replaces the old top StatusBar. Shows mode/hints on the left
// and token counts + connection status on the right.
// Progressively collapses as the terminal narrows.

use crate::{
    app::state::{AgentMode, AppState, TurnStatus},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Paragraph, Widget},
};
use unicode_width::UnicodeWidthStr;

pub struct FooterLine<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> FooterLine<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            state,
            theme,
            strings,
        }
    }
}

impl Widget for FooterLine<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height == 0 || area.width < 4 {
            return;
        }

        let width = area.width as usize;

        // ── Right side: tokens + connection ─────────────────────────────────
        let token_str = self.state.token_tracker.format_compact();
        let conn_str = if self.state.connected {
            self.strings.connected
        } else {
            self.strings.disconnected
        };
        let conn_style = if self.state.connected {
            self.theme.success
        } else {
            self.theme.error
        };

        // Build right spans from outer to inner for progressive collapse.
        // Level 0 (full): "{tokens} · {connection}"
        // Level 1: "{tokens}"
        // Level 2: (nothing)
        let right_full = if token_str.is_empty() {
            format!(" {conn_str} ")
        } else {
            format!(" {token_str} · {conn_str} ")
        };

        // ── Left side: contextual hint ───────────────────────────────────────
        let is_running = self.state.turn_status == TurnStatus::Running;
        let has_draft = !self.state.input_text.is_empty();
        let quit_pending = self
            .state
            .last_interrupt_at
            .is_some_and(|t| t.elapsed().as_secs_f32() < 1.0);

        let (left_hint, left_style) = if quit_pending {
            (self.strings.quit_confirm_hint, self.theme.error)
        } else if has_draft && is_running {
            (self.strings.tab_to_queue, self.theme.dim)
        } else if has_draft {
            (self.strings.enter_to_send_hint, self.theme.dim)
        } else if is_running {
            (self.strings.esc_to_interrupt, self.theme.dim)
        } else {
            // Idle, empty input: show shortcut hint + mode indicator.
            ("", self.theme.dim) // handled specially below
        };

        let mode_style = match self.state.mode {
            AgentMode::Agent => self.theme.input_border_agent,
            AgentMode::Plan => self.theme.input_border_plan,
        };
        let mode_label = match self.state.mode {
            AgentMode::Agent => self.strings.mode_agent,
            AgentMode::Plan => self.strings.mode_plan,
        };
        let effective_model = self
            .state
            .current_model_override
            .as_deref()
            .or(self.state.workspace_model.as_deref())
            .unwrap_or(self.strings.model_default_label);

        // Determine which left content to render based on available width.
        // Progressive collapse: full → mode-only → empty
        let right_w = right_full.width();

        // Full left: "? for shortcuts · Mode (shift+tab to cycle)"
        let left_full = if !left_hint.is_empty() {
            format!("  {left_hint}")
        } else {
            format!(
                "  {} · {} · {} ({})",
                self.strings.shortcuts_hint, mode_label, effective_model, self.strings.mode_cycle_hint
            )
        };

        // Medium left: just "Mode" or just the hint
        let left_medium = if !left_hint.is_empty() {
            format!("  {left_hint}")
        } else {
            format!("  {} · {} · {}", self.strings.shortcuts_hint, mode_label, effective_model)
        };

        // Short left: just hint or mode
        let left_short = if !left_hint.is_empty() {
            format!("  {left_hint}")
        } else {
            format!("  {} · {}", mode_label, effective_model)
        };

        let left_full_w = left_full.width();
        let left_medium_w = left_medium.width();
        let left_short_w = left_short.width();

        // Try fitting full left + full right, then progressively collapse.
        let (left_text, show_right, right_text) = if left_full_w + right_w <= width {
            (left_full, true, right_full)
        } else if left_medium_w + right_w <= width {
            (left_medium, true, right_full)
        } else if left_short_w + right_w <= width {
            (left_short, true, right_full)
        } else if left_full_w <= width {
            (left_full, false, String::new())
        } else if left_medium_w <= width {
            (left_medium, false, String::new())
        } else if left_short_w <= width {
            (left_short, false, String::new())
        } else {
            (String::new(), false, String::new())
        };

        let left_w = left_text.width();

        // Build the line.
        if left_text.is_empty() && !show_right {
            return;
        }

        let left_style_final = if !left_hint.is_empty() && !quit_pending {
            left_style
        } else if quit_pending {
            left_style
        } else {
            self.theme.dim
        };

        let mut spans: Vec<Span<'static>> = Vec::new();

        // Left content with optional mode coloring.
        if left_hint.is_empty() && !quit_pending {
            // Idle state: color the mode label differently.
            let parts = left_text.splitn(2, mode_label).collect::<Vec<_>>();
            if parts.len() == 2 {
                spans.push(Span::styled(parts[0].to_string(), self.theme.dim));
                spans.push(Span::styled(mode_label.to_string(), mode_style));
                spans.push(Span::styled(parts[1].to_string(), self.theme.dim));
            } else {
                spans.push(Span::styled(left_text.clone(), self.theme.dim));
            }
        } else {
            spans.push(Span::styled(left_text.clone(), left_style_final));
        }

        if show_right {
            // Pad between left and right.
            let pad = width.saturating_sub(left_w + right_text.width());
            spans.push(Span::raw(" ".repeat(pad)));

            // Right: tokens (dim) then " · " then connection (colored).
            if !token_str.is_empty() && self.state.connected {
                spans.push(Span::styled(format!(" {token_str} · "), self.theme.dim));
                spans.push(Span::styled(format!("{conn_str} "), conn_style));
            } else if !token_str.is_empty() {
                spans.push(Span::styled(format!(" {token_str} · "), self.theme.dim));
                spans.push(Span::styled(format!("{conn_str} "), conn_style));
            } else {
                spans.push(Span::styled(format!(" {conn_str} "), conn_style));
            }
        }

        Paragraph::new(Line::from(spans)).render(area, buf);
    }
}
