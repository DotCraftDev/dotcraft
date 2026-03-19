// PlanPanel widget — renders plan/updated snapshots as a themed todo list.

use crate::{app::state::AppState, i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Block, Borders, Paragraph, Widget},
};

pub struct PlanPanel<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> PlanPanel<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings }
    }
}

impl Widget for PlanPanel<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let Some(plan) = &self.state.plan else {
            return;
        };

        let mut lines: Vec<Line> = Vec::new();

        // Overview (dim, single line)
        if !plan.overview.is_empty() {
            lines.push(Line::from(Span::styled(
                format!("  {}", plan.overview),
                self.theme.dim,
            )));
            lines.push(Line::default());
        }

        for todo in &plan.todos {
            let (icon, style) = match todo.status.as_str() {
                "completed" => ("✅", self.theme.tool_completed),
                "in_progress" => ("🔄", self.theme.tool_active),
                "cancelled" => ("🚫", self.theme.dim),
                _ => ("⬜", self.theme.agent_message),
            };
            // Truncate long content to fit the panel
            let max_len = area.width.saturating_sub(6) as usize;
            let content = if todo.content.chars().count() > max_len {
                let truncated: String = todo.content.chars().take(max_len.saturating_sub(1)).collect();
                format!("{truncated}…")
            } else {
                todo.content.clone()
            };
            lines.push(Line::from(vec![
                Span::raw(" "),
                Span::raw(icon),
                Span::raw(" "),
                Span::styled(content, style),
            ]));
        }

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.dim)
            .title(Span::styled(
                format!(" {}{} ", self.strings.plan_title_prefix, plan.title),
                self.theme.dim,
            ));

        Paragraph::new(lines).block(block).render(area, buf);
    }
}
