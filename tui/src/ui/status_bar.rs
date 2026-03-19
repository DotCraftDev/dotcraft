// StatusBar widget (§8.1 of specs/tui-client.md).
// Phase 2: turn status spinner, system status indicator, right-aligned token counter,
// full theme integration.

use crate::{
    app::state::{AgentMode, AppState, TurnStatus},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::Widget,
};
use unicode_width::UnicodeWidthStr;

/// Braille spinner frames (same as ChatView).
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

pub struct StatusBar<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> StatusBar<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings }
    }
}

impl Widget for StatusBar<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        // ── Left section ──────────────────────────────────────────────────
        let thread_label = self
            .state
            .current_thread_name
            .as_deref()
            .or(self.state.current_thread_id.as_deref())
            .unwrap_or("no thread");

        let (mode_label, mode_style) = match self.state.mode {
            AgentMode::Agent => (self.strings.mode_agent, self.theme.status_bar_mode_agent),
            AgentMode::Plan => (self.strings.mode_plan, self.theme.status_bar_mode_plan),
        };

        // ── Turn/system status ─────────────────────────────────────────────
        let status_span = if let Some(sys) = &self.state.system_status {
            let label = match sys.kind.as_str() {
                "compacting" => self.strings.system_compacting,
                "consolidating" => self.strings.system_consolidating,
                _ => "⟳ Processing...",
            };
            Span::styled(label, self.theme.dim)
        } else {
            match self.state.turn_status {
                TurnStatus::Running => {
                    let frame = SPINNER[self.state.tick_count as usize % SPINNER.len()];
                    Span::styled(
                        format!("{frame} {}", self.strings.turn_running),
                        self.theme.tool_active,
                    )
                }
                TurnStatus::WaitingApproval => {
                    Span::styled(self.strings.turn_approval, self.theme.approval_border)
                }
                TurnStatus::Idle => Span::raw(""),
            }
        };

        // ── Right section ─────────────────────────────────────────────────
        let token_label = self.state.token_tracker.format_compact();
        let conn_label = if self.state.connected {
            self.strings.connected
        } else {
            self.strings.disconnected
        };
        let conn_style = if self.state.connected {
            self.theme.status_bar_conn
        } else {
            self.theme.error
        };

        // Build left spans.
        let mut left: Vec<Span> = vec![
            Span::styled(" ✦ DotCraft ", self.theme.status_bar_brand),
            Span::styled("─ ", self.theme.dim),
            Span::raw(thread_label.to_string()),
            Span::styled(" ─ ", self.theme.dim),
            Span::styled(mode_label, mode_style),
        ];
        let status_text = status_span.content.clone();
        if !status_text.is_empty() {
            left.push(Span::styled(" ─ ", self.theme.dim));
            left.push(status_span);
        }

        // Build right spans.
        let right: Vec<Span> = vec![
            Span::styled(
                format!("{} {}", token_label, self.strings.tokens_label),
                self.theme.status_bar_tokens,
            ),
            Span::styled(" ─ ", self.theme.dim),
            Span::styled(conn_label, conn_style),
            Span::raw(" "),
        ];

        // Use display width so CJK thread names are measured correctly.
        let left_len: usize = left.iter().map(|s| s.content.width()).sum();
        let right_len: usize = right.iter().map(|s| s.content.width()).sum();
        let total_width = area.width as usize;
        let pad = total_width.saturating_sub(left_len + right_len);

        let mut all_spans = left;
        all_spans.push(Span::raw(" ".repeat(pad)));
        all_spans.extend(right);

        let line = Line::from(all_spans).style(self.theme.status_bar_bg);
        line.render(area, buf);
    }
}
