// ChatView widget — scrollable conversation history (§8.3 of specs/tui-client.md).
// Design: Calling/Called tool format, elapsed time, adaptive wrapping,
// default-visible result summaries, inline SubAgent block, inline Plan block.

use crate::{
    app::state::{AppState, HistoryEntry, TurnStatus},
    app::token_tracker::format_token_count,
    i18n::Strings,
    theme::Theme,
    ui::markdown,
};
use ratatui::{
    buffer::Buffer,
    layout::Rect,
    text::{Line, Span},
    widgets::{Block, Borders, Paragraph, Widget, Wrap},
};
use std::time::Duration;
use unicode_width::UnicodeWidthChar;

/// Braille spinner frames for animated tool calls.
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

/// Maximum result/output lines shown below a completed tool call.
const TOOL_CALL_MAX_LINES: usize = 5;

pub struct ChatView<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
    /// Available width (for markdown rendering and scroll tracking).
    width: u16,
}

impl<'a> ChatView<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self {
            state,
            theme,
            strings,
            width: 80,
        }
    }

    pub fn with_width(mut self, w: u16) -> Self {
        self.width = w;
        self
    }
}

impl Widget for ChatView<'_> {
    fn render(self, area: Rect, buf: &mut Buffer) {
        // 2 cols for the left gutter, 2 for potential right margin.
        let render_width = area.width.saturating_sub(4).max(20);

        let mut lines: Vec<Line<'static>> = Vec::new();

        // ── Committed history entries ──────────────────────────────────────
        let history = &self.state.history;
        for (i, entry) in history.iter().enumerate() {
            // Emit a turn separator when transitioning from agent/tool → user.
            let prev_is_agent_or_tool = i > 0
                && matches!(
                    &history[i - 1],
                    HistoryEntry::AgentMessage { .. } | HistoryEntry::ToolCall { .. }
                );
            if prev_is_agent_or_tool && matches!(entry, HistoryEntry::UserMessage { .. }) {
                self.render_turn_separator(render_width, &mut lines);
            }

            self.render_history_entry(entry, render_width, &mut lines);
        }

        // ── Active streaming section ───────────────────────────────────────
        if self.state.turn_status == TurnStatus::Running
            || self.state.turn_status == TurnStatus::WaitingApproval
        {
            self.render_streaming(render_width, &mut lines);
        }

        // ── Active tool calls ──────────────────────────────────────────────
        for tool in &self.state.streaming.active_tools {
            if !tool.completed {
                self.render_active_tool(tool, render_width, &mut lines);
            }
        }

        // ── Inline SubAgent block ──────────────────────────────────────────
        if !self.state.subagent_entries.is_empty() {
            self.render_inline_subagents(render_width, &mut lines);
        }

        // ── Inline Plan block ─────────────────────────────────────────────
        if let Some(plan) = &self.state.plan {
            // Only render inline plan during active streaming or when it's the
            // last entry — otherwise it shows up as committed history.
            if self.state.turn_status == TurnStatus::Running {
                self.render_inline_plan(plan, render_width, &mut lines);
            }
        }

        // ── Compute scroll ────────────────────────────────────────────────
        // Use Paragraph::line_count() instead of lines.len() so that visual
        // rows produced by word-wrap are counted correctly. lines.len() only
        // counts Line objects; each Line that overflows the viewport width
        // wraps into additional visual rows that lines.len() misses, causing
        // max_scroll to be too small and content to be unreachable.
        let para = Paragraph::new(lines.clone())
            .block(Block::default().borders(Borders::NONE))
            .wrap(Wrap { trim: false });
        let total_visual_lines = para.line_count(area.width);

        // Record the viewport height so the input router can use it for
        // viewport-relative PageUp/PageDown scrolling. Cell allows mutation
        // through the shared &AppState reference.
        self.state.last_viewport_height.set(area.height as usize);

        let viewport = area.height as usize;
        let max_scroll = total_visual_lines.saturating_sub(viewport);
        let clamped_offset = self.state.scroll_offset.min(max_scroll);

        let scroll = if self.state.at_bottom {
            max_scroll
        } else {
            max_scroll.saturating_sub(clamped_offset)
        };

        // ── Scroll indicator ──────────────────────────────────────────────
        let visible_bottom = scroll + viewport;
        let lines_below = total_visual_lines.saturating_sub(visible_bottom);
        if lines_below > 0 {
            let indicator = Line::from(Span::styled(
                format!(" ↓ {lines_below} more lines "),
                self.theme.dim,
            ));
            lines.push(indicator);
        }

        // Clamp scroll to u16::MAX — Paragraph::scroll() takes (u16, u16) and
        // very long sessions could theoretically exceed that range.
        let scroll_u16 = scroll.min(u16::MAX as usize) as u16;

        Paragraph::new(lines)
            .block(Block::default().borders(Borders::NONE))
            .wrap(Wrap { trim: false })
            .scroll((scroll_u16, 0))
            .render(area, buf);
    }
}

impl ChatView<'_> {
    // ── Turn separator ──────────────────────────────────────────────────────

    fn render_turn_separator(&self, width: u16, out: &mut Vec<Line<'static>>) {
        let rule_width = width.saturating_sub(2) as usize;
        out.push(Line::from(Span::styled(
            format!("  {}", "─".repeat(rule_width)),
            self.theme.dim,
        )));
        out.push(Line::default());
    }

    // ── History entry rendering ──────────────────────────────────────────────

    fn render_history_entry(&self, entry: &HistoryEntry, width: u16, out: &mut Vec<Line<'static>>) {
        match entry {
            HistoryEntry::UserMessage { text } => {
                out.push(Line::default());
                let prefix_str = format!("{} ", self.strings.user_prefix);
                let prefix_width = display_width(&prefix_str);
                let indent = " ".repeat(prefix_width);

                for (i, line) in text.lines().enumerate() {
                    if i == 0 {
                        out.push(Line::from(vec![
                            Span::styled(prefix_str.clone(), self.theme.user_message),
                            Span::styled(line.to_string(), self.theme.user_message),
                        ]));
                    } else {
                        out.push(Line::from(vec![
                            Span::raw(indent.clone()),
                            Span::styled(line.to_string(), self.theme.user_message),
                        ]));
                    }
                }
                out.push(Line::default());
            }

            HistoryEntry::AgentMessage { text } => {
                let rendered = markdown::render_owned(text.clone(), width, self.theme);
                for (i, mut line) in rendered.into_iter().enumerate() {
                    let prefix = if i == 0 {
                        Span::styled("• ", self.theme.dim)
                    } else {
                        Span::raw("  ")
                    };
                    line.spans.insert(0, prefix);
                    out.push(line);
                }
                out.push(Line::default());
            }

            HistoryEntry::ToolCall {
                name,
                args,
                result,
                success,
                duration,
                ..
            } => {
                self.render_committed_tool(
                    name,
                    args,
                    result.as_deref(),
                    *success,
                    *duration,
                    width,
                    out,
                );
            }

            HistoryEntry::Error { message } => {
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::styled(format!("{} ", self.strings.error_prefix), self.theme.error),
                    Span::styled(message.clone(), self.theme.error),
                ]));
                out.push(Line::default());
            }

            HistoryEntry::SystemInfo { message } => {
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::styled(message.clone(), self.theme.system_info),
                ]));
            }
        }
    }

    // ── Tool call rendering ────────────────────────────────────────────────

    /// Format tool call arguments as a compact inline invocation.
    fn format_invocation(name: &str, args: &str) -> String {
        if args.is_empty() {
            return format!("{name}()");
        }
        // Try to extract a compact single-argument display from JSON.
        // If args is a JSON object with a single string value, show it quoted.
        if let Ok(v) = serde_json::from_str::<serde_json::Value>(args) {
            if let Some(obj) = v.as_object() {
                if obj.len() == 1 {
                    let val = obj.values().next().unwrap();
                    if let Some(s) = val.as_str() {
                        return format!("{name}(\"{s}\")");
                    }
                }
            }
        }
        // Fallback: truncated raw args.
        let compact = truncate(args, 60);
        format!("{name}({compact})")
    }

    /// Render an active (in-flight) tool call.
    fn render_active_tool(
        &self,
        tool: &crate::app::state::ActiveToolCall,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        let frame = SPINNER[self.state.tick_count as usize % SPINNER.len()];
        let invocation = Self::format_invocation(&tool.tool_name, &tool.arguments);

        // "⠋ Calling ToolName("arg")"
        let prefix = format!("  {frame} {} ", self.strings.calling);
        let prefix_w = display_width(&prefix);
        let available = (width as usize).saturating_sub(prefix_w);

        if display_width(&invocation) <= available {
            // Fits on one line.
            out.push(Line::from(vec![
                Span::styled(prefix, self.theme.tool_active),
                Span::styled(invocation, self.theme.tool_active),
            ]));
        } else {
            // Overflow: header on one line, invocation wrapped on next with └.
            out.push(Line::from(Span::styled(
                format!("  {frame} {}…", self.strings.calling),
                self.theme.tool_active,
            )));
            out.push(Line::from(vec![
                Span::styled("    └ ".to_string(), self.theme.dim),
                Span::styled(
                    truncate(&invocation, width.saturating_sub(6) as usize),
                    self.theme.dim,
                ),
            ]));
        }
    }

    /// Render a committed (completed or failed) tool call.
    fn render_committed_tool(
        &self,
        name: &str,
        args: &str,
        result: Option<&str>,
        success: bool,
        duration: Option<Duration>,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        let bullet = if success { "•" } else { "•" };
        let bullet_style = if success {
            self.theme.success
        } else {
            self.theme.tool_error
        };
        let verb = self.strings.called;

        let invocation = Self::format_invocation(name, args);
        let elapsed = duration
            .map(|d| format!(" ({:.1}s)", d.as_secs_f64()))
            .unwrap_or_default();

        // "• Called ToolName("arg") (0.3s)"
        let header = format!("  {bullet} {verb} ");
        let header_w = display_width(&header);
        let suffix = elapsed.clone();
        let available = (width as usize).saturating_sub(header_w + suffix.len());

        let inline = display_width(&invocation) <= available;

        if inline {
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(format!("{bullet} "), bullet_style),
                Span::styled(format!("{verb} "), self.theme.dim),
                Span::styled(invocation.clone(), self.theme.tool_completed),
                Span::styled(elapsed, self.theme.dim),
            ]));
        } else {
            // Header line
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(format!("{bullet} "), bullet_style),
                Span::styled(format!("{verb}"), self.theme.dim),
                Span::styled(elapsed, self.theme.dim),
            ]));
            // Wrapped invocation
            out.push(Line::from(vec![
                Span::styled("    └ ".to_string(), self.theme.dim),
                Span::styled(
                    truncate(&invocation, width.saturating_sub(6) as usize),
                    self.theme.tool_completed,
                ),
            ]));
        }

        // Result summary (always visible, dimmed, max TOOL_CALL_MAX_LINES).
        if let Some(result_text) = result {
            if !result_text.is_empty() {
                let result_lines: Vec<&str> = result_text.lines().collect();
                let show_count = result_lines.len().min(TOOL_CALL_MAX_LINES);
                let last_shown = show_count.saturating_sub(1);
                for (i, line) in result_lines.iter().take(show_count).enumerate() {
                    let is_last = i == last_shown;
                    let truncated_suffix = if is_last && result_lines.len() > TOOL_CALL_MAX_LINES {
                        "…"
                    } else {
                        ""
                    };
                    let text = truncate(line, width.saturating_sub(8) as usize);
                    let prefix_str = if is_last && inline {
                        "    └ "
                    } else {
                        "    │ "
                    };
                    out.push(Line::from(vec![
                        Span::styled(prefix_str.to_string(), self.theme.dim),
                        Span::styled(format!("{text}{truncated_suffix}"), self.theme.dim),
                    ]));
                }
            }
        }
    }

    // ── Streaming section ───────────────────────────────────────────────────

    fn render_streaming(&self, width: u16, out: &mut Vec<Line<'static>>) {
        // ── Reasoning block ────────────────────────────────────────────────
        if !self.state.streaming.reasoning_buffer.is_empty() {
            let header = if self.state.show_reasoning {
                format!("  {} ", self.strings.reasoning_header)
            } else {
                format!("  {} (Tab to expand)", self.strings.reasoning_header)
            };
            out.push(Line::from(Span::styled(
                header,
                self.theme.reasoning_header,
            )));

            if self.state.show_reasoning {
                for line in self.state.streaming.reasoning_buffer.lines() {
                    out.push(Line::from(vec![
                        Span::raw("    "),
                        Span::styled(line.to_string(), self.theme.reasoning),
                    ]));
                }
                out.push(Line::default());
            }
        }

        // ── Agent message (full re-render from message_buffer) ─────────────
        if !self.state.streaming.message_buffer.is_empty() {
            let rendered = markdown::render_owned(
                self.state.streaming.message_buffer.clone(),
                width,
                self.theme,
            );
            for (i, mut line) in rendered.into_iter().enumerate() {
                let prefix = if i == 0 {
                    Span::styled("• ", self.theme.dim)
                } else {
                    Span::raw("  ")
                };
                line.spans.insert(0, prefix);
                out.push(line);
            }
            // Streaming cursor indicator
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled("▍", self.theme.agent_message),
            ]));
        } else if self.state.streaming.reasoning_buffer.is_empty()
            && self.state.streaming.active_tools.is_empty()
        {
            // Nothing yet — the StatusIndicator above the input shows "Working",
            // but we also keep a subtle indicator in the chat area.
            let frame = SPINNER[self.state.tick_count as usize % SPINNER.len()];
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(format!("{frame}"), self.theme.dim),
            ]));
        }
    }

    // ── Inline SubAgent block ───────────────────────────────────────────────

    fn render_inline_subagents(&self, width: u16, out: &mut Vec<Line<'static>>) {
        let entries = &self.state.subagent_entries;
        if entries.is_empty() {
            return;
        }

        let active_count = entries.iter().filter(|e| !e.is_completed).count();
        let done_count = entries.iter().filter(|e| e.is_completed).count();

        // When all are complete, collapse to a summary line.
        if active_count == 0 {
            let total_in: i64 = entries.iter().map(|e| e.input_tokens).sum();
            let total_out: i64 = entries.iter().map(|e| e.output_tokens).sum();
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled("✓ ", self.theme.success),
                Span::styled(
                    format!(
                        "{done_count} {} (↑{} ↓{})",
                        self.strings.subagents_complete,
                        format_token_count(total_in),
                        format_token_count(total_out),
                    ),
                    self.theme.dim,
                ),
            ]));
            return;
        }

        // Header
        let rule_w = width.saturating_sub(4) as usize;
        let header = format!("──── SubAgents ({active_count} active, {done_count} done)");
        let header = truncate(&header, rule_w);
        out.push(Line::from(Span::styled(header, self.theme.dim)));

        let tick = self.state.tick_count as usize;
        for entry in entries {
            let (status_span, name_style) = if entry.is_completed {
                (
                    Span::styled("•  ".to_string(), self.theme.success),
                    self.theme.dim,
                )
            } else if entry.current_tool.is_some() {
                let frame = SPINNER[tick % SPINNER.len()];
                (
                    Span::styled(format!("{frame}  "), self.theme.tool_active),
                    self.theme.tool_active,
                )
            } else {
                let frame = SPINNER[tick % SPINNER.len()];
                (
                    Span::styled(format!("{frame}  "), self.theme.dim),
                    self.theme.dim,
                )
            };

            let tool_text = if entry.is_completed {
                "Done".to_string()
            } else {
                entry
                    .current_tool
                    .clone()
                    .unwrap_or_else(|| "…".to_string())
            };

            let tokens = format!(
                "↑{} ↓{}",
                format_token_count(entry.input_tokens),
                format_token_count(entry.output_tokens),
            );

            // Layout: "  status  label  tool  tokens"
            let label_w = 16usize;
            let tool_w = 20usize;
            let label = truncate_pad(&entry.label, label_w);
            let tool = truncate_pad(&tool_text, tool_w);

            out.push(Line::from(vec![
                Span::raw("  "),
                status_span,
                Span::styled(label, name_style),
                Span::raw("  "),
                Span::styled(tool, self.theme.dim),
                Span::raw("  "),
                Span::styled(tokens, self.theme.dim),
            ]));
        }
    }

    // ── Inline Plan block ────────────────────────────────────────────────────

    fn render_inline_plan(
        &self,
        plan: &crate::app::state::PlanSnapshot,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        let rule_w = width.saturating_sub(4) as usize;
        let header = format!("──── Plan: {}", plan.title);
        out.push(Line::from(Span::styled(
            truncate(&header, rule_w),
            self.theme.dim,
        )));

        for todo in &plan.todos {
            let (icon, style) = match todo.status.as_str() {
                "completed" => ("✅", self.theme.tool_completed),
                "in_progress" => ("🔄", self.theme.tool_active),
                "cancelled" => ("🚫", self.theme.dim),
                _ => ("⬜", self.theme.agent_message),
            };
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::raw(format!("{icon}  ")),
                Span::styled(
                    truncate(&todo.content, width.saturating_sub(8) as usize),
                    style,
                ),
            ]));
        }
        out.push(Line::default());
    }
}

/// Truncate `s` to at most `max_cols` display columns. Appends '…' if truncated.
/// Truncate `s` to at most `max_cols` display columns. Appends '…' if truncated.
fn truncate(s: &str, max_cols: usize) -> String {
    if max_cols == 0 {
        return String::new();
    }
    // First check if the string fits without truncation.
    let total_width: usize = s
        .chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum();
    if total_width <= max_cols {
        return s.to_string();
    }
    // Doesn't fit — truncate and append '…'.
    let mut width: usize = 0;
    let mut out = String::new();
    for c in s.chars() {
        let cw = UnicodeWidthChar::width(c).unwrap_or(0);
        if width + cw > max_cols.saturating_sub(1) {
            out.push('…');
            return out;
        }
        out.push(c);
        width += cw;
    }
    out
}

/// Truncate to `max_cols` and right-pad with spaces to reach exactly `max_cols`.
fn truncate_pad(s: &str, max_cols: usize) -> String {
    let truncated = truncate(s, max_cols);
    let w = display_width(&truncated);
    format!("{truncated}{}", " ".repeat(max_cols.saturating_sub(w)))
}

/// Total display width of a string (sum of per-char widths).
fn display_width(s: &str) -> usize {
    s.chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum()
}
