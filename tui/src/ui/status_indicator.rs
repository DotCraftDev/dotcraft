// StatusIndicator widget (§8.4.1 of specs/tui-client.md).
// Shows "Working (Ns · esc to interrupt)" above the InputEditor while a turn is running.
// Includes a shimmer animation on the "Working" label text.

use crate::{
    app::state::{AppState, TurnStatus},
    i18n::Strings,
    theme::Theme,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    style::Modifier,
    text::{Line, Span},
    widgets::{Paragraph, Widget},
};

/// Braille spinner frames.
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

/// Number of ticks for one full shimmer wave cycle.
const SHIMMER_PERIOD: u64 = 12;

pub struct StatusIndicator<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
}

impl<'a> StatusIndicator<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings }
    }

    /// Returns how many rows this widget needs. 0 if it should not be shown.
    pub fn preferred_height(state: &AppState) -> u16 {
        if state.turn_status == TurnStatus::Running || state.system_status.is_some() {
            1
        } else {
            0
        }
    }
}

impl Widget for StatusIndicator<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        if area.height == 0 || area.width < 10 {
            return;
        }

        let tick = self.state.tick_count;
        let frame = SPINNER[tick as usize % SPINNER.len()];

        // Compute elapsed seconds from turn_started_at.
        let elapsed_secs = self
            .state
            .turn_started_at
            .map(|start| start.elapsed().as_secs())
            .unwrap_or(0);
        let elapsed_str = format_elapsed(elapsed_secs);

        // Determine label text.
        let label = if let Some(sys) = &self.state.system_status {
            match sys.kind.as_str() {
                "compacting" => self.strings.system_compacting,
                "consolidating" => self.strings.system_consolidating,
                _ => self.strings.working,
            }
        } else {
            self.strings.working
        };

        // Build spans: spinner + shimmer label + elapsed + interrupt hint.
        let mut spans: Vec<Span<'static>> = Vec::new();
        spans.push(Span::raw("  "));
        spans.push(Span::styled(
            format!("{frame} "),
            self.theme.status_indicator,
        ));

        // Shimmer: each character of the label gets a brightness based on position + tick.
        for (i, ch) in label.chars().enumerate() {
            let phase = ((i as u64 + tick) % SHIMMER_PERIOD) as f32 / SHIMMER_PERIOD as f32;
            // Sine-like brightness: oscillate between dim (0.4) and bright (1.0).
            let t = (phase * std::f32::consts::TAU).sin() * 0.5 + 0.5; // 0..1
            let style = if t > 0.65 {
                self.theme.status_indicator.add_modifier(Modifier::BOLD)
            } else {
                self.theme.status_indicator
            };
            spans.push(Span::styled(ch.to_string(), style));
        }

        // Elapsed + interrupt hint in dim parentheses.
        let hint = format!("  ({elapsed_str} · {})", self.strings.esc_to_interrupt);
        spans.push(Span::styled(hint, self.theme.dim));

        let line = Line::from(spans);
        Paragraph::new(line).render(area, buf);
    }
}

/// Format elapsed seconds compactly: "5s", "1m 03s", "1h 02m 03s".
pub fn format_elapsed(secs: u64) -> String {
    if secs < 60 {
        format!("{secs}s")
    } else if secs < 3600 {
        let m = secs / 60;
        let s = secs % 60;
        format!("{m}m {s:02}s")
    } else {
        let h = secs / 3600;
        let m = (secs % 3600) / 60;
        let s = secs % 60;
        format!("{h}h {m:02}m {s:02}s")
    }
}
