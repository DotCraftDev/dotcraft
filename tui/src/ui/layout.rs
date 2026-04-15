// Screen zone layout computation (§7.1 of specs/tui-client.md).
// New design: 2-zone layout. ChatView takes all available space; BottomPane
// is self-sizing (StatusIndicator + PendingInputPreview + InputEditor + FooterLine).

use ratatui::layout::Rect;

/// Output of the layout computation — the two primary zones plus sub-zones.
pub struct Zones {
    pub chat_view: Rect,
    /// Shown above InputEditor only while a turn is running.
    pub status_indicator: Option<Rect>,
    /// Shown between StatusIndicator and InputEditor when pending_input is non-empty.
    pub pending_preview: Option<Rect>,
    pub input_editor: Rect,
    /// Single footer line below InputEditor. None on compact terminals.
    pub footer: Option<Rect>,
}

/// Compute the screen layout.
///
/// - `turn_running`: whether to reserve space for the `StatusIndicator`.
/// - `has_pending`: whether to reserve space for `PendingInputPreview`.
/// - `input_height`: desired InputEditor height in rows (content lines only, no separator).
/// - `status_indicator_lines`: number of lines the StatusIndicator needs (default 1).
pub fn compute(
    area: Rect,
    turn_running: bool,
    has_pending: bool,
    input_height: u16,
    status_indicator_lines: u16,
) -> Zones {
    let compact = area.height < 20;

    // On compact terminals suppress extra chrome.
    let input_h = if compact {
        1
    } else {
        input_height.clamp(1, 10)
    };
    // Keep footer visible even on compact terminals so connection/thread state is always visible.
    let footer_h: u16 = 1;
    let status_h: u16 = if turn_running && !compact {
        status_indicator_lines.max(1)
    } else {
        0
    };
    let pending_h: u16 = if has_pending && !compact { 1 } else { 0 };

    let bottom_h = status_h + pending_h + input_h + footer_h;

    // Vertical split: ChatView (flex) | BottomPane (fixed)
    let chat_h = area.height.saturating_sub(bottom_h);

    let chat_view = Rect {
        x: area.x,
        y: area.y,
        width: area.width,
        height: chat_h,
    };

    let mut y = area.y + chat_h;

    let status_indicator = if status_h > 0 {
        let r = Rect {
            x: area.x,
            y,
            width: area.width,
            height: status_h,
        };
        y += status_h;
        Some(r)
    } else {
        None
    };

    let pending_preview = if pending_h > 0 {
        let r = Rect {
            x: area.x,
            y,
            width: area.width,
            height: pending_h,
        };
        y += pending_h;
        Some(r)
    } else {
        None
    };

    let input_editor = Rect {
        x: area.x,
        y,
        width: area.width,
        height: input_h,
    };
    y += input_h;

    let footer = if footer_h > 0 {
        Some(Rect {
            x: area.x,
            y,
            width: area.width,
            height: footer_h,
        })
    } else {
        None
    };

    Zones {
        chat_view,
        status_indicator,
        pending_preview,
        input_editor,
        footer,
    }
}

/// Compute the preferred InputEditor height from the number of content lines.
/// No separator row is included (separator was removed in the new design).
pub fn input_preferred_height(content_lines: usize) -> u16 {
    (content_lines.max(1) as u16).clamp(1, 10)
}

/// Compute the preferred StatusIndicator height from the number of detail lines
/// (header line + wrapped detail lines).
pub fn status_indicator_height(detail_lines: usize) -> u16 {
    (1 + detail_lines as u16).min(4)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compact_layout_keeps_footer_visible() {
        let area = Rect {
            x: 0,
            y: 0,
            width: 80,
            height: 10,
        };
        let zones = compute(area, false, false, 1, 1);
        assert!(zones.footer.is_some());
        assert_eq!(zones.footer.expect("footer").height, 1);
    }
}
