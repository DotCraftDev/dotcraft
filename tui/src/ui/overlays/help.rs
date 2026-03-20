// HelpOverlay widget — shown when the user runs /help or presses F1/?.
// Displays a two-section reference: slash commands and key bindings.

use crate::{i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::{Constraint, Direction, Layout, Rect},
    text::{Line, Span},
    widgets::{Block, Borders, Clear, Paragraph, Widget},
};

/// All slash commands with their descriptions (static, language-independent).
const COMMANDS: &[(&str, &str)] = &[
    ("/help", "Show this help overlay"),
    ("/new", "Start a new thread"),
    ("/sessions", "Browse and resume previous threads"),
    ("/load <id>", "Resume a thread by ID"),
    ("/plan", "Switch to Plan mode"),
    ("/agent", "Switch to Agent mode"),
    ("/clear", "Clear the chat display"),
    ("/cron", "List cron jobs"),
    ("/heartbeat", "Trigger heartbeat run"),
    ("/quit", "Exit dotcraft-tui"),
];

/// All key bindings with their descriptions (static, language-independent).
const KEYBINDINGS: &[(&str, &str)] = &[
    ("Enter", "Submit input"),
    ("Shift+Enter", "Insert newline"),
    ("Shift+Tab", "Cycle Agent/Plan mode"),
    ("Ctrl+C", "Interrupt / quit (double-press)"),
    ("Ctrl+D", "Quit"),
    ("Ctrl+L", "Redraw terminal"),
    ("Ctrl+V", "Paste from clipboard (in editor)"),
    ("Esc", "Switch focus to chat view"),
    ("i / Enter", "Switch focus to input (from chat)"),
    ("Tab", "Toggle reasoning visibility (in chat)"),
    ("e", "Toggle tool call details (in chat)"),
    ("y", "Copy last agent message to clipboard (in chat)"),
    ("F1 / ?", "Open this help overlay (in chat)"),
    ("↑/↓", "History (in editor) / scroll lines (in chat)"),
    ("PageUp/Down", "Scroll by page (in chat)"),
    ("Home/End", "Top/bottom (chat) or line start/end (editor)"),
];

pub struct HelpOverlay<'a> {
    pub theme: &'a Theme,
    pub strings: &'a Strings,
}

impl<'a> HelpOverlay<'a> {
    pub fn new(theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { theme, strings }
    }

    /// Centered popup: 60% width, 80% height.
    pub fn popup_area(full: Rect) -> Rect {
        let popup_width = (full.width * 60 / 100).max(60).min(full.width);
        let popup_height = (full.height * 80 / 100).max(12).min(full.height.saturating_sub(2));
        let x = full.x + (full.width.saturating_sub(popup_width)) / 2;
        let y = full.y + (full.height.saturating_sub(popup_height)) / 2;
        Rect { x, y, width: popup_width, height: popup_height }
    }
}

impl Widget for HelpOverlay<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let popup = Self::popup_area(area);
        Clear.render(popup, buf);

        let block = Block::default()
            .borders(Borders::ALL)
            .border_style(self.theme.approval_border)
            .title(Line::from(Span::styled(
                format!(" {} ", self.strings.help_title),
                self.theme.approval_border,
            )));

        let inner = block.inner(popup);
        block.render(popup, buf);

        if inner.height < 4 {
            return;
        }

        // Split into two vertical halves: commands (top) and key bindings (bottom).
        let cmd_rows = COMMANDS.len() as u16 + 2; // +2 for header + blank line
        let kb_rows = KEYBINDINGS.len() as u16 + 2;
        let total = cmd_rows + kb_rows;

        let chunks = if total <= inner.height {
            Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Length(cmd_rows), Constraint::Min(kb_rows)])
                .split(inner)
        } else {
            // Not enough space — split evenly.
            Layout::default()
                .direction(Direction::Vertical)
                .constraints([Constraint::Percentage(50), Constraint::Percentage(50)])
                .split(inner)
        };

        let cmd_area = chunks[0];
        let kb_area = chunks[1];

        // ── Commands section ──────────────────────────────────────────────
        let mut cmd_lines: Vec<Line> = vec![
            Line::from(Span::styled(
                format!("  {}", self.strings.help_commands_header),
                self.theme.approval_border,
            )),
            Line::default(),
        ];
        let key_col = 18usize;
        for (cmd, desc) in COMMANDS {
            let padding = key_col.saturating_sub(cmd.len());
            cmd_lines.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(cmd.to_string(), self.theme.dim),
                Span::raw(" ".repeat(padding)),
                Span::styled(desc.to_string(), self.theme.agent_message),
            ]));
        }
        Paragraph::new(cmd_lines).render(cmd_area, buf);

        // ── Key bindings section ──────────────────────────────────────────
        let mut kb_lines: Vec<Line> = vec![
            Line::from(Span::styled(
                format!("  {}", self.strings.help_keybindings_header),
                self.theme.approval_border,
            )),
            Line::default(),
        ];
        let kb_key_col = 18usize;
        for (key, desc) in KEYBINDINGS {
            let padding = kb_key_col.saturating_sub(key.len());
            kb_lines.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(key.to_string(), self.theme.dim),
                Span::raw(" ".repeat(padding)),
                Span::styled(desc.to_string(), self.theme.agent_message),
            ]));
        }

        // Footer dismiss hint
        kb_lines.push(Line::default());
        kb_lines.push(Line::from(Span::styled(
            "  Esc / q / ? to close",
            self.theme.dim,
        )));

        Paragraph::new(kb_lines).render(kb_area, buf);
    }
}
