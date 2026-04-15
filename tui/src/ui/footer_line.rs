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
use unicode_width::{UnicodeWidthChar, UnicodeWidthStr};

pub struct FooterLine<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum RightVariant {
    Full,
    ThreadConn,
    ConnOnly,
    None,
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

        // ── Right side: thread + tokens + connection ───────────────────────
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
        let thread_source = self
            .state
            .current_thread_name
            .as_deref()
            .or(self.state.current_thread_id.as_deref())
            .unwrap_or(self.strings.footer_no_thread);
        // Keep thread labels readable and bounded in narrow terminals.
        let thread_str = truncate_display(thread_source, 28);

        // Build right variants and choose by progressive collapse.
        // Priority: keep thread+connection visible; drop tokens first.
        let right_full = if token_str.is_empty() {
            format!(" {thread_str} · {conn_str} ")
        } else {
            format!(" {thread_str} · {token_str} · {conn_str} ")
        };
        let right_thread_conn = format!(" {thread_str} · {conn_str} ");
        let right_conn_only = format!(" {conn_str} ");

        // ── Left side: contextual hint ───────────────────────────────────────
        let is_running = self.state.turn_status == TurnStatus::Running
            || self.state.turn_status == TurnStatus::WaitingApproval;
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
        let left_variants = [
            (left_full, left_full_w),
            (left_medium, left_medium_w),
            (left_short, left_short_w),
            (String::new(), 0),
        ];

        let Some((left_text, left_w, right_variant)) = select_layout(
            width,
            &left_variants,
            right_full.width(),
            right_thread_conn.width(),
            right_conn_only.width(),
        ) else {
            return;
        };
        let show_right = right_variant != RightVariant::None;

        let right_w = match right_variant {
            RightVariant::Full => right_full.width(),
            RightVariant::ThreadConn => right_thread_conn.width(),
            RightVariant::ConnOnly => right_conn_only.width(),
            RightVariant::None => 0,
        };

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
            let pad = width.saturating_sub(left_w + right_w);
            spans.push(Span::raw(" ".repeat(pad)));

            match right_variant {
                RightVariant::Full => {
                    spans.push(Span::styled(format!(" {thread_str}"), self.theme.footer_context));
                    if !token_str.is_empty() {
                        spans.push(Span::styled(" · ".to_string(), self.theme.dim));
                        spans.push(Span::styled(token_str.clone(), self.theme.dim));
                    }
                    spans.push(Span::styled(" · ".to_string(), self.theme.dim));
                    spans.push(Span::styled(conn_str.to_string(), conn_style));
                    spans.push(Span::raw(" "));
                }
                RightVariant::ThreadConn => {
                    spans.push(Span::styled(format!(" {thread_str}"), self.theme.footer_context));
                    spans.push(Span::styled(" · ".to_string(), self.theme.dim));
                    spans.push(Span::styled(conn_str.to_string(), conn_style));
                    spans.push(Span::raw(" "));
                }
                RightVariant::ConnOnly => {
                    spans.push(Span::styled(format!(" {conn_str} "), conn_style));
                }
                RightVariant::None => {}
            }
        }

        Paragraph::new(Line::from(spans)).render(area, buf);
    }
}

fn truncate_display(text: &str, max_width: usize) -> String {
    if max_width == 0 {
        return String::new();
    }
    if text.width() <= max_width {
        return text.to_string();
    }
    if max_width <= 3 {
        return ".".repeat(max_width);
    }
    let mut out = String::new();
    let mut used = 0usize;
    let limit = max_width - 3;
    for ch in text.chars() {
        let cw = UnicodeWidthChar::width(ch).unwrap_or(0);
        if used + cw > limit {
            break;
        }
        out.push(ch);
        used += cw;
    }
    out.push_str("...");
    out
}

fn select_layout(
    width: usize,
    left_variants: &[(String, usize); 4],
    right_full_w: usize,
    right_thread_conn_w: usize,
    right_conn_only_w: usize,
) -> Option<(String, usize, RightVariant)> {
    let right_variants = [
        (RightVariant::Full, right_full_w),
        (RightVariant::ThreadConn, right_thread_conn_w),
        (RightVariant::ConnOnly, right_conn_only_w),
        (RightVariant::None, 0),
    ];
    for (right_variant, right_w) in right_variants {
        for (left_text, left_w) in left_variants {
            if left_w + right_w <= width {
                return Some((left_text.clone(), *left_w, right_variant));
            }
        }
    }
    None
}

#[cfg(test)]
mod tests {
    use super::{select_layout, truncate_display, RightVariant};

    #[test]
    fn truncate_display_keeps_short_text() {
        assert_eq!(truncate_display("thread_123", 20), "thread_123");
    }

    #[test]
    fn truncate_display_applies_ellipsis() {
        assert_eq!(truncate_display("thread_20260415_0ueyzv", 12), "thread_20...");
    }

    #[test]
    fn select_layout_drops_tokens_before_thread_conn() {
        let left_variants = [
            ("left full".to_string(), 9),
            ("left med".to_string(), 8),
            ("left short".to_string(), 10),
            ("".to_string(), 0),
        ];
        let selected = select_layout(26, &left_variants, 32, 16, 10).expect("layout selected");
        assert_eq!(selected.2, RightVariant::ThreadConn);
    }
}
