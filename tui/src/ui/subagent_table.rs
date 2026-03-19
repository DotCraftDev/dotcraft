// SubAgentTable widget — renders subagent/progress snapshots as a live table.
// Phase 3: theme integration, animated spinner for thinking agents.

use crate::{app::state::AppState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::{Constraint, Rect},
    text::Span,
    widgets::{Block, Borders, Row, Table, Widget},
};

/// Braille spinner frames (same as ChatView/StatusBar).
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

pub struct SubAgentTable<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> SubAgentTable<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings }
    }
}

impl Widget for SubAgentTable<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let tick = self.state.tick_count as usize;

        let rows: Vec<Row> = self
            .state
            .subagent_entries
            .iter()
            .map(|e| {
                let (tool_label, row_style) = if e.is_completed {
                    ("● Done".to_string(), self.theme.dim)
                } else if let Some(tool) = &e.current_tool {
                    (tool.clone(), self.theme.agent_message)
                } else {
                    // Thinking: animated spinner
                    let frame = SPINNER[tick % SPINNER.len()];
                    (format!("{frame} …"), self.theme.tool_active)
                };
                Row::new(vec![
                    e.label.clone(),
                    tool_label,
                    format_tokens(e.input_tokens),
                    format_tokens(e.output_tokens),
                ])
                .style(row_style)
            })
            .collect();

        let header_style = self.theme.dim;
        let header = Row::new(vec!["Label", "Tool", "↑In", "↓Out"]).style(header_style);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.dim)
            .title(Span::styled(
                format!(" {} ", self.strings.subagents_title),
                self.theme.dim,
            ));

        Table::new(
            rows,
            [
                Constraint::Min(12),
                Constraint::Min(10),
                Constraint::Length(6),
                Constraint::Length(6),
            ],
        )
        .header(header)
        .block(block)
        .render(area, buf);
    }
}

fn format_tokens(n: i64) -> String {
    if n >= 1_000_000 {
        format!("{:.1}M", n as f64 / 1_000_000.0)
    } else if n >= 1_000 {
        format!("{:.1}k", n as f64 / 1_000.0)
    } else {
        n.to_string()
    }
}
