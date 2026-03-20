// WelcomeScreen widget (§8.1 of specs/tui-client.md).
// Full-screen startup widget shown while the Wire Protocol handshake is in progress.
// Auto-dismisses once the connection is ready; any key press also dismisses it.

use crate::{i18n::Strings, theme::Theme};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Paragraph, Widget, Wrap},
};

/// Minimum terminal dimensions required to render the ASCII art logo.
const MIN_LOGO_WIDTH: u16 = 60;
const MIN_LOGO_HEIGHT: u16 = 20;

/// Braille spinner frames.
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

/// ASCII art DotCraft logo (8 lines wide, ~52 cols).
const LOGO: &[&str] = &[
    "  ██████╗  ██████╗ ████████╗ ██████╗██████╗  █████╗ ███████╗████████╗",
    "  ██╔══██╗██╔═══██╗╚══██╔══╝██╔════╝██╔══██╗██╔══██╗██╔════╝╚══██╔══╝",
    "  ██║  ██║██║   ██║   ██║   ██║     ██████╔╝███████║█████╗     ██║   ",
    "  ██║  ██║██║   ██║   ██║   ██║     ██╔══██╗██╔══██║██╔══╝     ██║   ",
    "  ██████╔╝╚██████╔╝   ██║   ╚██████╗██║  ██║██║  ██║██║        ██║   ",
    "  ╚═════╝  ╚═════╝    ╚═╝    ╚═════╝╚═╝  ╚═╝╚═╝  ╚═╝╚═╝        ╚═╝   ",
];

pub struct WelcomeScreen<'a> {
    pub version: &'a str,
    pub workspace: &'a str,
    pub connected: bool,
    pub tick_count: u64,
    pub theme: &'a Theme,
    pub strings: &'a Strings,
}

impl<'a> WelcomeScreen<'a> {
    pub fn new(
        version: &'a str,
        workspace: &'a str,
        connected: bool,
        tick_count: u64,
        theme: &'a Theme,
        strings: &'a Strings,
    ) -> Self {
        Self { version, workspace, connected, tick_count, theme, strings }
    }
}

impl Widget for WelcomeScreen<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        let mut lines: Vec<Line<'static>> = Vec::new();

        let show_logo = area.width >= MIN_LOGO_WIDTH && area.height >= MIN_LOGO_HEIGHT;

        if show_logo {
            // Blank line before logo for vertical padding.
            lines.push(Line::default());
            for logo_line in LOGO {
                lines.push(Line::from(Span::styled(
                    logo_line.to_string(),
                    self.theme.welcome_brand.add_modifier(Modifier::BOLD),
                )));
            }
            lines.push(Line::default());
        } else {
            lines.push(Line::default());
        }

        // "Welcome to DotCraft v{version}"
        lines.push(Line::from(vec![
            Span::raw("  "),
            Span::raw("Welcome to "),
            Span::styled(
                "DotCraft".to_string(),
                self.theme.welcome_brand.add_modifier(Modifier::BOLD),
            ),
            Span::raw(format!(" v{}", self.version)),
        ]));

        // Workspace path (dim)
        let ws_display = if self.workspace.is_empty() { "(none)" } else { self.workspace };
        lines.push(Line::from(vec![
            Span::raw("  "),
            Span::styled(
                format!("Workspace: {ws_display}"),
                self.theme.dim,
            ),
        ]));

        lines.push(Line::default());

        // Quick-start hints
        lines.push(Line::from(vec![
            Span::raw("  "),
            Span::styled(
                self.strings.welcome_hint_start,
                self.theme.dim,
            ),
        ]));
        lines.push(Line::from(vec![
            Span::raw("  "),
            Span::styled(
                self.strings.welcome_hint_commands,
                self.theme.dim,
            ),
        ]));

        lines.push(Line::default());

        // Connection status line at the bottom
        if self.connected {
            lines.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(
                    self.strings.welcome_ready,
                    self.theme.success,
                ),
            ]));
        } else {
            let frame = SPINNER[self.tick_count as usize % SPINNER.len()];
            lines.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(
                    format!("{frame} {}", self.strings.welcome_connecting),
                    self.theme.dim,
                ),
            ]));
        }

        Paragraph::new(lines)
            .wrap(Wrap { trim: false })
            .render(area, buf);
    }
}
