// Slash command parsing and dispatch.

/// Represents a parsed slash command.
#[derive(Debug)]
pub enum SlashCommand {
    Help,
    Sessions,
    New,
    Load { thread_id: String },
    Plan,
    Agent,
    Clear,
    Cron,
    Heartbeat,
    Quit,
    Unknown { name: String },
}

/// Try to parse a slash command from user input.
/// Returns None if the input is not a slash command.
pub fn parse(input: &str) -> Option<SlashCommand> {
    let input = input.trim();
    if !input.starts_with('/') {
        return None;
    }

    let mut parts = input.splitn(2, ' ');
    let cmd = parts.next().unwrap_or("").to_lowercase();
    let arg = parts.next().map(str::trim).unwrap_or("");

    Some(match cmd.as_str() {
        "/help" => SlashCommand::Help,
        "/sessions" => SlashCommand::Sessions,
        "/new" => SlashCommand::New,
        "/load" => SlashCommand::Load {
            thread_id: arg.to_string(),
        },
        "/plan" => SlashCommand::Plan,
        "/agent" => SlashCommand::Agent,
        "/clear" => SlashCommand::Clear,
        "/cron" => SlashCommand::Cron,
        "/heartbeat" => SlashCommand::Heartbeat,
        "/quit" | "/exit" => SlashCommand::Quit,
        other => SlashCommand::Unknown {
            name: other.trim_start_matches('/').to_string(),
        },
    })
}
