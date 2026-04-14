// CommandPopup widget — slash command completion popup above the input editor.
// Shown when the user types a `/` prefix; filters by prefix match.

use crate::{
    app::state::{CommandPopupState, SlashCommandDescriptor},
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};

/// Filter commands by prefix and return matching (command, description) pairs.
pub fn filter_commands(
    input: &str,
    commands: &[SlashCommandDescriptor],
) -> Vec<(String, String)> {
    let prefix = input.trim_start_matches('/');
    commands
        .iter()
        .filter(|cmd| {
            let name = cmd.name.trim_start_matches('/');
            name.starts_with(prefix)
        })
        .map(|cmd| (cmd.name.clone(), cmd.description.clone()))
        .collect()
}

pub struct CommandPopup<'a> {
    pub popup_state: &'a CommandPopupState,
    pub theme: &'a Theme,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn filters_dynamic_commands_by_prefix() {
        let commands = vec![
            SlashCommandDescriptor::new("/help", "Show help", "local-ui"),
            SlashCommandDescriptor::new("/code-review", "Custom review", "custom"),
            SlashCommandDescriptor::new("/cron", "List cron", "builtin"),
        ];
        let filtered = filter_commands("/co", &commands);
        assert_eq!(filtered, vec![("/code-review".to_string(), "Custom review".to_string())]);
    }
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
