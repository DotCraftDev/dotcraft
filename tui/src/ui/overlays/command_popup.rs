// CommandPopup widget — slash command completion popup above the input editor.
// Shown when the user types a `/` prefix; filters by prefix match.

use crate::{app::state::CommandPopupState, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};

/// All slash commands with descriptions for the completion popup.
pub const COMMANDS: &[(&str, &str)] = &[
    ("/help", "Show help overlay"),
    ("/new", "Start a new thread"),
    ("/sessions", "Browse previous threads"),
    ("/load", "Resume a thread by ID"),
    ("/plan", "Switch to Plan mode"),
    ("/agent", "Switch to Agent mode"),
    ("/clear", "Clear terminal screen"),
    ("/cron", "List cron jobs"),
    ("/heartbeat", "Trigger heartbeat"),
    ("/model", "Select or set model"),
    ("/quit", "Exit dotcraft-tui"),
];

/// Filter commands by prefix and return matching (command, description) pairs.
pub fn filter_commands(input: &str) -> Vec<(String, String)> {
    let prefix = input.trim_start_matches('/');
    COMMANDS
        .iter()
        .filter(|(cmd, _)| {
            let name = cmd.trim_start_matches('/');
            name.starts_with(prefix)
        })
        .map(|(cmd, desc)| (cmd.to_string(), desc.to_string()))
        .collect()
}

pub struct CommandPopup<'a> {
    pub popup_state: &'a CommandPopupState,
    pub theme: &'a Theme,
}

impl<'a> CommandPopup<'a> {
    pub fn new(popup_state: &'a CommandPopupState, theme: &'a Theme) -> Self {
        Self { popup_state, theme }
    }

    /// Compute the popup area just above the input editor.
    pub fn popup_area(input_rect: Rect, item_count: usize) -> Rect {
        let height = (item_count as u16 + 2).min(14).min(input_rect.y);
        let width = input_rect.width.min(50);
        Rect {
            x: input_rect.x,
            y: input_rect.y.saturating_sub(height),
            width,
            height,
        }
    }
}

impl Widget for CommandPopup<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        Clear.render(area, buf);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.dim);
        let inner = block.inner(area);
        block.render(area, buf);

        if inner.height == 0 || inner.width < 5 {
            return;
        }

        let cmd_col = 14usize;
        let lines: Vec<Line> = self
            .popup_state
            .items
            .iter()
            .enumerate()
            .map(|(i, (cmd, desc))| {
                let is_selected = i == self.popup_state.selected;
                let padding = cmd_col.saturating_sub(cmd.len());
                let style = if is_selected {
                    self.theme.agent_message.add_modifier(Modifier::REVERSED)
                } else {
                    self.theme.agent_message
                };
                let desc_style = if is_selected {
                    self.theme.dim.add_modifier(Modifier::REVERSED)
                } else {
                    self.theme.dim
                };
                Line::from(vec![
                    Span::styled(format!(" {cmd}"), style),
                    Span::styled(" ".repeat(padding), desc_style),
                    Span::styled(desc.clone(), desc_style),
                ])
            })
            .collect();

        Paragraph::new(lines).render(inner, buf);
    }
}
