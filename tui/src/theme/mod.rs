pub mod config;
pub mod default;

use anyhow::Result;
use config::ThemeConfig;
use ratatui::style::{Color, Modifier, Style};
use std::path::{Path, PathBuf};

/// Resolved theme — all semantic styles pre-computed as `ratatui::Style`.
/// Created once at startup and passed by reference into widgets and renderers.
#[derive(Debug, Clone)]
pub struct Theme {
    // Chat history
    pub user_message: Style,
    pub agent_message: Style,
    pub reasoning: Style,
    pub reasoning_header: Style,

    // Tool calls
    pub tool_active: Style,
    pub tool_completed: Style,
    pub tool_error: Style,

    // Headings
    pub heading1: Style,
    pub heading2: Style,
    pub heading3: Style,

    // Inline and block code
    pub inline_code: Style,
    pub code_block_border: Style,

    // Table
    pub table_border: Style,
    pub table_header: Style,

    // Blockquote
    pub blockquote_border: Style,
    pub blockquote_text: Style,

    // Links
    pub link: Style,

    // Status / errors
    pub error: Style,
    pub success: Style,
    pub dim: Style,
    pub system_info: Style,

    // Footer line (replaces old status bar)
    pub footer_fg: Style,
    pub footer_context: Style,

    // Status indicator ("Working" label)
    pub status_indicator: Style,

    // Welcome screen brand color
    pub welcome_brand: Style,

    // Input editor
    pub input_border_agent: Style,
    pub input_border_plan: Style,
    pub input_placeholder: Style,

    // Approval overlay
    pub approval_border: Style,

    // syntect theme name for code block highlighting
    pub syntect_theme: String,
}

impl Default for Theme {
    fn default() -> Self {
        Self::dark()
    }
}

impl Theme {
    /// Built-in dark theme.
    pub fn dark() -> Self {
        Self {
            user_message: Style::default().fg(Color::Cyan),
            agent_message: Style::default().fg(Color::White),
            reasoning: Style::default()
                .fg(Color::DarkGray)
                .add_modifier(Modifier::ITALIC),
            reasoning_header: Style::default()
                .fg(Color::Cyan)
                .add_modifier(Modifier::ITALIC),

            tool_active: Style::default().fg(Color::Yellow),
            tool_completed: Style::default().fg(Color::DarkGray),
            tool_error: Style::default().fg(Color::Red),

            heading1: Style::default()
                .fg(Color::Rgb(124, 58, 237))
                .add_modifier(Modifier::BOLD),
            heading2: Style::default()
                .fg(Color::Cyan)
                .add_modifier(Modifier::BOLD),
            heading3: Style::default()
                .fg(Color::White)
                .add_modifier(Modifier::BOLD),

            inline_code: Style::default()
                .fg(Color::Rgb(250, 200, 100))
                .bg(Color::Rgb(40, 40, 50)),
            code_block_border: Style::default().fg(Color::DarkGray),

            table_border: Style::default().fg(Color::DarkGray),
            table_header: Style::default()
                .fg(Color::White)
                .add_modifier(Modifier::BOLD),

            blockquote_border: Style::default().fg(Color::DarkGray),
            blockquote_text: Style::default()
                .fg(Color::Gray)
                .add_modifier(Modifier::ITALIC),

            link: Style::default()
                .fg(Color::Blue)
                .add_modifier(Modifier::UNDERLINED),

            error: Style::default().fg(Color::Red),
            success: Style::default().fg(Color::Green),
            dim: Style::default().fg(Color::DarkGray),
            system_info: Style::default().fg(Color::DarkGray),

            footer_fg: Style::default().fg(Color::DarkGray),
            footer_context: Style::default().fg(Color::DarkGray),

            status_indicator: Style::default().fg(Color::Yellow),

            welcome_brand: Style::default()
                .fg(Color::Rgb(124, 58, 237))
                .add_modifier(Modifier::BOLD),

            input_border_agent: Style::default().fg(Color::Green),
            input_border_plan: Style::default().fg(Color::Blue),
            input_placeholder: Style::default().fg(Color::DarkGray),

            approval_border: Style::default()
                .fg(Color::Yellow)
                .add_modifier(Modifier::BOLD),

            syntect_theme: "base16-ocean.dark".to_string(),
        }
    }

    /// Resolve the theme by trying CLI path, workspace path, user global path in order,
    /// then merging overrides on top of the built-in default.
    pub fn resolve(
        cli_path: Option<&Path>,
        workspace_path: Option<&Path>,
    ) -> Result<Self> {
        let mut config = default::default_toml_config();

        // Resolve candidate paths in ascending priority order so higher-priority
        // configs override lower-priority ones via merge.
        let mut paths: Vec<PathBuf> = Vec::new();

        // User-global config
        if let Some(config_dir) = dirs::config_dir() {
            paths.push(config_dir.join("dotcraft").join("tui-theme.toml"));
        }

        // Workspace config
        if let Some(ws) = workspace_path {
            paths.push(ws.join(".craft").join("tui-theme.toml"));
        }

        // CLI override (highest priority)
        if let Some(p) = cli_path {
            paths.push(p.to_path_buf());
        }

        for path in &paths {
            if path.exists() {
                match ThemeConfig::load_from_file(path) {
                    Ok(overrides) => {
                        tracing::debug!("Loaded theme from {}", path.display());
                        config.merge_with(overrides);
                    }
                    Err(e) => {
                        tracing::warn!("Failed to load theme from {}: {e}", path.display());
                    }
                }
            }
        }

        Ok(Self::from_config(&config))
    }

    fn from_config(cfg: &ThemeConfig) -> Self {
        let mut theme = Self::dark();

        if let Some(c) = cfg.colors.as_ref() {
            macro_rules! set_fg {
                ($field:ident, $opt:expr) => {
                    if let Some(color) = $opt.as_deref().and_then(parse_color) {
                        theme.$field = theme.$field.fg(color);
                    }
                };
            }
            set_fg!(user_message, c.user_message);
            set_fg!(agent_message, c.agent_message);
            set_fg!(reasoning, c.reasoning);
            set_fg!(tool_active, c.tool_active);
            set_fg!(tool_completed, c.tool_completed);
            set_fg!(error, c.error);
            set_fg!(success, c.success);
            set_fg!(dim, c.dim);
            set_fg!(approval_border, c.approval_border);
            set_fg!(heading1, c.heading1);
            set_fg!(heading2, c.heading2);
            set_fg!(heading3, c.heading3);
            set_fg!(code_block_border, c.code_block_border);
            set_fg!(table_border, c.table_border);
            set_fg!(blockquote_border, c.blockquote);
            set_fg!(link, c.link_color);

            if let Some(fg) = c.inline_code_fg.as_deref().and_then(parse_color) {
                theme.inline_code = theme.inline_code.fg(fg);
            }
            if let Some(bg) = c.inline_code_bg.as_deref().and_then(parse_color) {
                theme.inline_code = theme.inline_code.bg(bg);
            }
            if let Some(color) = c.mode_agent.as_deref().and_then(parse_color) {
                theme.input_border_agent = Style::default().fg(color);
            }
            if let Some(color) = c.mode_plan.as_deref().and_then(parse_color) {
                theme.input_border_plan = Style::default().fg(color);
            }
            if let Some(color) = c.brand.as_deref().and_then(parse_color) {
                theme.welcome_brand = theme.welcome_brand.fg(color);
            }
            set_fg!(status_indicator, c.status_indicator);
        }

        if let Some(footer) = cfg.footer.as_ref() {
            if let Some(color) = footer.foreground.as_deref().and_then(parse_color) {
                theme.footer_fg = theme.footer_fg.fg(color);
            }
            if let Some(color) = footer.context_color.as_deref().and_then(parse_color) {
                theme.footer_context = theme.footer_context.fg(color);
            }
        }

        if let Some(code) = cfg.code.as_ref() {
            if let Some(name) = &code.syntect_theme {
                theme.syntect_theme = name.clone();
            }
        }

        theme
    }
}

/// Parse a color string: ratatui named colors or `#RRGGBB` hex.
pub fn parse_color(s: &str) -> Option<Color> {
    if let Some(hex) = s.strip_prefix('#') {
        if hex.len() == 6 {
            let r = u8::from_str_radix(&hex[0..2], 16).ok()?;
            let g = u8::from_str_radix(&hex[2..4], 16).ok()?;
            let b = u8::from_str_radix(&hex[4..6], 16).ok()?;
            return Some(Color::Rgb(r, g, b));
        }
    }
    // ratatui named colors
    match s.to_lowercase().as_str() {
        "black" => Some(Color::Black),
        "red" => Some(Color::Red),
        "green" => Some(Color::Green),
        "yellow" => Some(Color::Yellow),
        "blue" => Some(Color::Blue),
        "magenta" => Some(Color::Magenta),
        "cyan" => Some(Color::Cyan),
        "gray" | "grey" => Some(Color::Gray),
        "dark_gray" | "darkgray" | "dark_grey" | "darkgrey" => Some(Color::DarkGray),
        "light_red" => Some(Color::LightRed),
        "light_green" => Some(Color::LightGreen),
        "light_yellow" => Some(Color::LightYellow),
        "light_blue" => Some(Color::LightBlue),
        "light_magenta" => Some(Color::LightMagenta),
        "light_cyan" => Some(Color::LightCyan),
        "white" => Some(Color::White),
        "reset" => Some(Color::Reset),
        _ => None,
    }
}
