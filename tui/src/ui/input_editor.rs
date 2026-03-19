// InputEditor widget (§8.4 of specs/tui-client.md).
// Codex-style borderless design: top separator/hint bar + mode gutter prefix.

use crate::{
    app::state::{AgentMode, AppState, FocusTarget},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Paragraph, Widget, Wrap},
};
use unicode_width::UnicodeWidthStr;

/// Width of the mode gutter prefix ("❯ " or "✎ ").
const GUTTER_COLS: u16 = 2;

pub struct InputEditor<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> InputEditor<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings }
    }

    /// Preferred height: 1 separator + content lines. Min 2, max 10.
    pub fn preferred_height(state: &AppState) -> u16 {
        let content_lines = state.input_line_count().max(1) as u16;
        (content_lines + 1).clamp(2, 10)
    }
}

impl Widget for InputEditor<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height < 2 || area.width < 6 {
            return;
        }

        let is_focused = self.state.focus == FocusTarget::InputEditor;
        let mode_style = match self.state.mode {
            AgentMode::Agent => self.theme.input_border_agent,
            AgentMode::Plan => self.theme.input_border_plan,
        };

        // ── Top separator / hint bar ────────────────────────────
        render_separator(
            buf,
            Rect { height: 1, ..area },
            self.state,
            self.theme,
            self.strings,
            is_focused,
        );

        // ── Text area (below separator) ─────────────────────────
        let text_y = area.y + 1;
        let text_h = area.height.saturating_sub(1);
        if text_h == 0 {
            return;
        }

        // Render mode gutter on the first text line.
        let gutter_str = match self.state.mode {
            AgentMode::Agent => "❯ ",
            AgentMode::Plan => "✎ ",
        };
        buf.set_string(area.x, text_y, gutter_str, mode_style);

        // Continuation gutter for lines 2+.
        for row in 1..text_h {
            buf.set_string(area.x, text_y + row, "  ", self.theme.dim);
        }

        // Inner rect for text content (after gutter).
        let inner = Rect {
            x: area.x + GUTTER_COLS,
            y: text_y,
            width: area.width.saturating_sub(GUTTER_COLS),
            height: text_h,
        };

        if self.state.input_text.is_empty() {
            Paragraph::new(Line::from(Span::styled(
                self.strings.placeholder,
                self.theme.input_placeholder,
            )))
            .render(inner, buf);
        } else {
            let lines: Vec<Line> = self
                .state
                .input_text
                .lines()
                .map(|l| Line::from(l.to_string()))
                .collect();
            let lines = if lines.is_empty() {
                vec![Line::default()]
            } else {
                lines
            };
            Paragraph::new(lines)
                .wrap(Wrap { trim: false })
                .render(inner, buf);
        }
    }
}

/// Render the horizontal separator with embedded hint spans.
fn render_separator(
    buf: &mut Buffer,
    area: Rect,
    state: &AppState,
    theme: &Theme,
    strings: &Strings,
    is_focused: bool,
) {
    let dim = theme.dim;
    let mode_style = match state.mode {
        AgentMode::Agent => theme.input_border_agent,
        AgentMode::Plan => theme.input_border_plan,
    };

    // Fill entire row with dim horizontal line.
    for x in area.x..area.x + area.width {
        if let Some(cell) = buf.cell_mut((x, area.y)) {
            cell.set_symbol("─").set_style(dim);
        }
    }

    let mode_label = match state.mode {
        AgentMode::Agent => strings.mode_agent,
        AgentMode::Plan => strings.mode_plan,
    };

    // Left side: "─ ? for shortcuts · Agent (shift+tab to cycle) "
    let left_spans: Vec<(&str, ratatui::style::Style)> = vec![
        ("─ ", dim),
        (strings.shortcuts_hint, dim.add_modifier(Modifier::DIM)),
        (" · ", dim),
        (mode_label, mode_style),
        (" (", dim),
        (strings.mode_cycle_hint, dim),
        (") ", dim),
    ];

    let mut col = area.x + 1; // start after initial "─"
    for (text, style) in &left_spans {
        let w = text.width() as u16;
        if col + w > area.x + area.width {
            break;
        }
        buf.set_string(col, area.y, text, *style);
        col += w;
    }

    // Right side: " enter to send " or focus hint.
    let right_text = if is_focused {
        strings.enter_to_send
    } else {
        strings.focus_chat_hint
    };
    let right_padded = format!(" {right_text} ─");
    let right_w = right_padded.width() as u16;
    let right_start = (area.x + area.width).saturating_sub(right_w);
    if right_start > col + 1 {
        buf.set_string(right_start, area.y, &right_padded, dim);
    }
}

/// Compute 2D cursor position (row, col) from a flat byte offset in a multi-line string.
/// `col` is the display-column offset (CJK characters count as 2 columns).
pub fn offset_to_2d(text: &str, byte_offset: usize) -> (u16, u16) {
    let safe_offset = byte_offset.min(text.len());
    let prefix = &text[..safe_offset];
    let row = prefix.matches('\n').count() as u16;

    // The current line text after the last newline (or the whole prefix if no newline).
    let line_text = match prefix.rfind('\n') {
        Some(pos) => &prefix[pos + 1..],
        None => prefix,
    };

    // Use display width so CJK characters (2 cols) position the cursor correctly.
    let col = line_text.width() as u16;

    (row, col)
}
