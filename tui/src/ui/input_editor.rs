// InputEditor widget (§8.4.3 of specs/tui-client.md).
// No top separator — hints moved to FooterLine below.
// Mode gutter prefix on the left; placeholder when empty.

use crate::{
    app::state::{AgentMode, AppState},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Paragraph, Widget, Wrap},
};

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

    /// Preferred height: content lines only (no separator row).
    /// Min 1, max 10.
    pub fn preferred_height(state: &AppState) -> u16 {
        (state.input_line_count().max(1) as u16).clamp(1, 10)
    }
}

impl Widget for InputEditor<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height == 0 || area.width < 4 {
            return;
        }

        let mode_style = match self.state.mode {
            AgentMode::Agent => self.theme.input_border_agent,
            AgentMode::Plan => self.theme.input_border_plan,
        };

        // Mode gutter on the first text line.
        let gutter_str = match self.state.mode {
            AgentMode::Agent => "❯ ",
            AgentMode::Plan => "✎ ",
        };
        buf.set_string(area.x, area.y, gutter_str, mode_style);

        // Continuation gutter for lines 2+.
        for row in 1..area.height {
            buf.set_string(area.x, area.y + row, "  ", self.theme.dim);
        }

        // Inner rect for text content (after gutter).
        let inner = Rect {
            x: area.x + GUTTER_COLS,
            y: area.y,
            width: area.width.saturating_sub(GUTTER_COLS),
            height: area.height,
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
            let lines = if lines.is_empty() { vec![Line::default()] } else { lines };
            Paragraph::new(lines)
                .wrap(Wrap { trim: false })
                .render(inner, buf);
        }
    }
}

/// Compute 2D cursor position (row, col) from a flat byte offset in a multi-line string.
/// `col` is the display-column offset (CJK characters count as 2 columns).
pub fn offset_to_2d(text: &str, byte_offset: usize) -> (u16, u16) {
    let safe_offset = byte_offset.min(text.len());
    let prefix = &text[..safe_offset];
    let row = prefix.matches('\n').count() as u16;

    let line_text = match prefix.rfind('\n') {
        Some(pos) => &prefix[pos + 1..],
        None => prefix,
    };

    use unicode_width::UnicodeWidthStr;
    let col = line_text.width() as u16;

    (row, col)
}
