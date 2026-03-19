// Screen zone layout computation (§7.1 of specs/tui-client.md).
// Phase 2: dynamic InputEditor height based on content line count.

use ratatui::layout::{Constraint, Direction, Layout, Rect};

/// Output of the layout computation — the four primary zones.
pub struct Zones {
    pub status_bar: Rect,
    pub chat_view: Rect,
    pub side_panel: Option<Rect>,
    pub input_editor: Rect,
}

/// Compute the screen layout given the terminal area, side panel visibility,
/// and the desired input editor height (from `InputEditor::preferred_height`).
///
/// `input_height` should be in the range [2, 10]; defaults to 2.
/// On short terminals (height < 20), input is forced to minimum (2 rows).
pub fn compute(area: Rect, show_side_panel: bool, input_height: u16) -> Zones {
    let input_h = if area.height < 20 {
        2
    } else {
        input_height.clamp(2, 10)
    };

    // Vertical split: StatusBar (1) | body | InputEditor (dynamic)
    let vertical = Layout::default()
        .direction(Direction::Vertical)
        .constraints([
            Constraint::Length(1),           // Status bar
            Constraint::Min(0),              // Body (gets remaining space)
            Constraint::Length(input_h),     // Input editor (dynamic)
        ])
        .split(area);

    let status_bar = vertical[0];
    let body = vertical[1];
    let input_editor = vertical[2];

    // Narrow terminal: always hide the side panel.
    let wide_enough = area.width >= 100;
    let show = show_side_panel && wide_enough;

    if show {
        let horizontal = Layout::default()
            .direction(Direction::Horizontal)
            .constraints([
                Constraint::Min(0),         // Chat view
                Constraint::Length(42),     // Side panel (~40 chars + borders)
            ])
            .split(body);
        Zones {
            status_bar,
            chat_view: horizontal[0],
            side_panel: Some(horizontal[1]),
            input_editor,
        }
    } else {
        Zones {
            status_bar,
            chat_view: body,
            side_panel: None,
            input_editor,
        }
    }
}
