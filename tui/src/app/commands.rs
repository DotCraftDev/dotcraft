use crate::{app::state::SlashCommandDescriptor, wire::types::CommandInfo};
use std::collections::HashSet;

/// Parsed slash command text (case-normalized name + argument forms).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParsedSlashCommand {
    pub name: String,
    pub argument_text: String,
    pub arguments: Vec<String>,
}

/// Slash commands that must remain client-local in TUI.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum LocalSlashCommand {
    Help,
    Sessions,
    Load { thread_id: String },
    Plan,
    Agent,
    Clear,
    Model { model_name: Option<String> },
    Quit,
}

/// Static local command metadata used by help/completion.
pub fn local_command_catalog() -> Vec<SlashCommandDescriptor> {
    vec![
        SlashCommandDescriptor::new("/help", "Show this help overlay", "local-ui"),
        SlashCommandDescriptor::new("/sessions", "Browse and resume previous threads", "local-ui"),
        SlashCommandDescriptor::new("/load", "Resume a thread by ID (/load <id>)", "local-ui"),
        SlashCommandDescriptor::new("/plan", "Switch to Plan mode", "local-ui"),
        SlashCommandDescriptor::new("/agent", "Switch to Agent mode", "local-ui"),
        SlashCommandDescriptor::new("/clear", "Clear the chat display", "local-ui"),
        SlashCommandDescriptor::new(
            "/model",
            "Open model picker or set model directly (/model [name|default])",
            "local-ui",
        ),
        SlashCommandDescriptor::new("/quit", "Exit dotcraft-tui", "local-ui"),
    ]
}

/// Merge local and server-provided commands with local names taking precedence.
pub fn merge_command_catalog(server_commands: &[CommandInfo]) -> Vec<SlashCommandDescriptor> {
    let mut merged = local_command_catalog();
    let mut known = HashSet::new();
    for cmd in &merged {
        known.insert(cmd.name.to_ascii_lowercase());
    }

    let mut server_sorted = server_commands.to_vec();
    server_sorted.sort_by_key(|c| c.name.to_ascii_lowercase());
    for cmd in server_sorted {
        let key = cmd.name.to_ascii_lowercase();
        if known.contains(&key) {
            continue;
        }
        merged.push(SlashCommandDescriptor::new(
            cmd.name,
            if cmd.description.trim().is_empty() {
                "(no description)"
            } else {
                cmd.description.as_str()
            },
            cmd.category,
        ));
        known.insert(key);
    }
    merged
}

/// Try to parse a slash command from user input.
/// Returns None if the input is not a slash command.
pub fn parse(input: &str) -> Option<ParsedSlashCommand> {
    let input = input.trim();
    if !input.starts_with('/') {
        return None;
    }

    let mut parts = input.splitn(2, ' ');
    let name = parts.next().unwrap_or("").to_lowercase();
    let argument_text = parts.next().map(str::trim).unwrap_or("").to_string();
    let arguments = argument_text
        .split_whitespace()
        .map(str::to_string)
        .collect::<Vec<_>>();

    Some(ParsedSlashCommand {
        name,
        argument_text,
        arguments,
    })
}

/// Map a parsed command to a local TUI command if applicable.
pub fn to_local_command(parsed: &ParsedSlashCommand) -> Option<LocalSlashCommand> {
    Some(match parsed.name.as_str() {
        "/help" => LocalSlashCommand::Help,
        "/sessions" => LocalSlashCommand::Sessions,
        "/load" => LocalSlashCommand::Load {
            thread_id: parsed.argument_text.clone(),
        },
        "/plan" => LocalSlashCommand::Plan,
        "/agent" => LocalSlashCommand::Agent,
        "/clear" => LocalSlashCommand::Clear,
        "/model" => LocalSlashCommand::Model {
            model_name: if parsed.argument_text.is_empty() {
                None
            } else {
                Some(parsed.argument_text.clone())
            },
        },
        "/quit" | "/exit" => LocalSlashCommand::Quit,
        _ => return None,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parse_slash_command_extracts_name_and_arguments() {
        let parsed = parse("/Code-Review src/lib.rs --strict").expect("should parse");
        assert_eq!(parsed.name, "/code-review");
        assert_eq!(parsed.argument_text, "src/lib.rs --strict");
        assert_eq!(parsed.arguments, vec!["src/lib.rs", "--strict"]);
    }

    #[test]
    fn merge_catalog_prefers_local_command_metadata() {
        let merged = merge_command_catalog(&[CommandInfo {
            name: "/help".to_string(),
            aliases: vec![],
            description: "Server help".to_string(),
            category: "builtin".to_string(),
            requires_admin: false,
        }]);
        let help = merged
            .iter()
            .find(|c| c.name == "/help")
            .expect("help should exist");
        assert_eq!(help.category, "local-ui");
        assert_eq!(help.description, "Show this help overlay");
    }
}
