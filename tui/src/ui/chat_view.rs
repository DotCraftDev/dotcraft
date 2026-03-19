// ChatView widget — scrollable conversation history (§8.2 of specs/tui-client.md).
// Phase 2: rich rendering with markdown, tool spinners, reasoning toggle, auto-scroll.
// Visual redesign: Codex-inspired gutters, turn separators, tree-style tool output.

use crate::{
    app::state::{AppState, HistoryEntry, TurnStatus},
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
use unicode_width::UnicodeWidthChar;

/// Braille spinner frames for animated tool calls.
const SPINNER: &[char] = &['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

pub struct ChatView<'a> {
    state: &'a AppState,
    theme: &'a Theme,
    strings: &'a Strings,
    /// Available width (for markdown rendering and scroll tracking).
    width: u16,
    /// When true, render plan/subagent inline (narrow terminal, no side panel).
    show_inline_panels: bool,
}

impl<'a> ChatView<'a> {
    pub fn new(state: &'a AppState, theme: &'a Theme, strings: &'a Strings) -> Self {
        Self { state, theme, strings, width: 80, show_inline_panels: false }
    }

    pub fn with_width(mut self, w: u16) -> Self {
        self.width = w;
        self
    }

    pub fn with_inline_panels(mut self, show: bool) -> Self {
        self.show_inline_panels = show;
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
            let prev_is_agent_or_tool = i > 0 && matches!(
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

        // ── Active tool spinners ───────────────────────────────────────────
        for tool in &self.state.streaming.active_tools {
            if !tool.completed {
                let frame = SPINNER[self.state.tick_count as usize % SPINNER.len()];
                let args_preview = truncate(&tool.arguments, 60);
                let line = Line::from(vec![
                    Span::styled("  ", self.theme.dim),
                    Span::styled(format!("{frame} "), self.theme.tool_active),
                    Span::styled(tool.tool_name.clone(), self.theme.tool_active),
                    Span::styled(
                        if args_preview.is_empty() {
                            String::new()
                        } else {
                            format!("  {args_preview}")
                        },
                        self.theme.dim,
                    ),
                ]);
                lines.push(line);
            }
        }

        // ── Inline panels (narrow terminal fallback) ──────────────────────
        if self.show_inline_panels {
            self.render_inline_panels(render_width, &mut lines);
        }

        // ── Compute scroll ────────────────────────────────────────────────
        // scroll_offset is "lines from bottom" (0 = at bottom).
        // Paragraph.scroll() expects "lines from top" (0 = at top).
        let total_lines = lines.len();
        let viewport = area.height as usize;
        let max_scroll = total_lines.saturating_sub(viewport);
        let clamped_offset = self.state.scroll_offset.min(max_scroll);

        let scroll = if self.state.at_bottom {
            max_scroll as u16
        } else {
            max_scroll.saturating_sub(clamped_offset) as u16
        };

        // ── Scroll indicator ──────────────────────────────────────────────
        let lines_below = total_lines.saturating_sub(scroll as usize + viewport);
        if lines_below > 0 {
            let indicator = Line::from(Span::styled(
                format!(" ↓ {} {} more lines ", self.strings.more_lines, lines_below),
                self.theme.dim,
            ));
            lines.push(indicator);
        }

        Paragraph::new(lines)
            .block(Block::default().borders(Borders::NONE))
            .wrap(Wrap { trim: false })
            .scroll((scroll, 0))
            .render(area, buf);
    }
}

impl ChatView<'_> {
    // ── Turn separator ──────────────────────────────────────────────────────

    fn render_turn_separator(&self, width: u16, out: &mut Vec<Line<'static>>) {
        // Dim horizontal rule spans the content width (excluding gutter).
        let rule_width = width.saturating_sub(2) as usize;
        out.push(Line::from(Span::styled(
            format!("  {}", "─".repeat(rule_width)),
            self.theme.dim,
        )));
        out.push(Line::default());
    }

    // ── History entry rendering ──────────────────────────────────────────────

    fn render_history_entry(
        &self,
        entry: &HistoryEntry,
        width: u16,
        out: &mut Vec<Line<'static>>,
    ) {
        match entry {
            HistoryEntry::UserMessage { text } => {
                // Blank line before user message for breathing room.
                out.push(Line::default());

                let prefix_str = format!("{} ", self.strings.user_prefix);
                // Continuation lines are indented by the same display width.
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

                // Add "• " gutter prefix to first line, "  " to the rest.
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

            HistoryEntry::ToolCall { name, args, result, success } => {
                let (icon, style) = if *success {
                    ("✓", self.theme.tool_completed)
                } else {
                    ("✗", self.theme.tool_error)
                };

                if self.state.tools_expanded {
                    // Expanded: icon + name + full args + full result
                    out.push(Line::from(vec![
                        Span::raw("  "),
                        Span::styled(format!("{icon} "), style),
                        Span::styled(name.clone(), style),
                    ]));
                    if !args.is_empty() {
                        let args_preview = truncate(args, width.saturating_sub(6) as usize);
                        out.push(Line::from(vec![
                            Span::raw("  "),
                            Span::styled("│ ", self.theme.dim),
                            Span::styled(args_preview, self.theme.dim),
                        ]));
                    }
                    if let Some(result_str) = result {
                        let result_lines: Vec<&str> = result_str.lines().collect();
                        let last_idx = result_lines.len().saturating_sub(1);
                        for (i, line) in result_lines.iter().enumerate() {
                            let prefix = if i == last_idx { "└ " } else { "│ " };
                            let text = truncate(line, width.saturating_sub(6) as usize);
                            out.push(Line::from(vec![
                                Span::raw("  "),
                                Span::styled(prefix, self.theme.dim),
                                Span::styled(text, self.theme.dim),
                            ]));
                        }
                    }
                } else {
                    // Collapsed: icon + name only
                    out.push(Line::from(vec![
                        Span::raw("  "),
                        Span::styled(format!("{icon} "), style),
                        Span::styled(name.clone(), style),
                    ]));
                }
            }

            HistoryEntry::Error { message } => {
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::styled(
                        format!("{} ", self.strings.error_prefix),
                        self.theme.error,
                    ),
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

    // ── Streaming section ───────────────────────────────────────────────────

    fn render_streaming(&self, width: u16, out: &mut Vec<Line<'static>>) {
        // ── Reasoning block ────────────────────────────────────────────────
        if !self.state.streaming.reasoning_buffer.is_empty() {
            let header = if self.state.show_reasoning {
                format!("  {} ", self.strings.reasoning_header)
            } else {
                format!("  {} (Tab to expand)", self.strings.reasoning_header)
            };
            out.push(Line::from(Span::styled(header, self.theme.reasoning_header)));

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
        // Always render the complete buffer through markdown on each frame.
        // Incremental commit breaks context-sensitive structures (tables, code blocks).
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
        } else if self.state.streaming.reasoning_buffer.is_empty() {
            // Nothing has arrived yet — show a working spinner.
            let frame = SPINNER[self.state.tick_count as usize % SPINNER.len()];
            out.push(Line::from(vec![
                Span::raw("  "),
                Span::styled(
                    format!("{frame} {}", self.strings.turn_running),
                    self.theme.tool_active,
                ),
            ]));
        }
    }

    // ── Inline panels (narrow terminal, no side panel) ──────────────────

    fn render_inline_panels(&self, width: u16, out: &mut Vec<Line<'static>>) {
        let rule_w = width.saturating_sub(2) as usize;

        // SubAgents (always shown when active)
        if !self.state.subagent_entries.is_empty() {
            out.push(Line::default());
            let header = format!(
                "──── {} ────",
                self.strings.subagents_title
            );
            out.push(Line::from(Span::styled(
                truncate(&header, rule_w),
                self.theme.dim,
            )));
            for entry in &self.state.subagent_entries {
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
                    crate::app::token_tracker::format_token_count(entry.input_tokens),
                    crate::app::token_tracker::format_token_count(entry.output_tokens),
                );
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::styled(
                        truncate(&entry.label, 20),
                        if entry.is_completed {
                            self.theme.success
                        } else {
                            self.theme.tool_active
                        },
                    ),
                    Span::raw("  "),
                    Span::styled(truncate(&tool_text, 20), self.theme.dim),
                    Span::raw("  "),
                    Span::styled(tokens, self.theme.dim),
                ]));
            }
        }

        // Plan
        if let Some(plan) = &self.state.plan {
            out.push(Line::default());
            let header = format!(
                "──── {}{} ────",
                self.strings.plan_title_prefix, plan.title
            );
            out.push(Line::from(Span::styled(
                truncate(&header, rule_w),
                self.theme.dim,
            )));
            for todo in &plan.todos {
                let icon = match todo.status.as_str() {
                    "completed" => "✅",
                    "in_progress" => "🔄",
                    "cancelled" => "🚫",
                    _ => "⬜",
                };
                out.push(Line::from(vec![
                    Span::raw("  "),
                    Span::raw(format!("{icon} ")),
                    Span::styled(
                        truncate(&todo.content, width.saturating_sub(6) as usize),
                        self.theme.agent_message,
                    ),
                ]));
            }
        }
    }
}

/// Truncate `s` to at most `max_cols` display columns. Appends '…' if truncated.
/// Uses `UnicodeWidthChar` so CJK characters (2 cols each) are handled correctly.
fn truncate(s: &str, max_cols: usize) -> String {
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

/// Total display width of a string (sum of per-char widths).
fn display_width(s: &str) -> usize {
    s.chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum()
}
