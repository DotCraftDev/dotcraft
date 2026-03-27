//! Human-readable tool invocation and result summaries aligned with DotCraft.Core
//! `ToolRegistry` / `CoreToolDisplays` (CLI `SessionHistoryPrinter` / `StreamAdapter`).

use serde_json::Value;
use unicode_width::UnicodeWidthChar;

/// Format a tool invocation line like `ToolRegistry.FormatToolCall` for known tools;
/// falls back to the legacy TUI heuristic (single string field or truncated JSON).
pub fn format_invocation_display(tool_name: &str, args: &str) -> String {
    if let Some(s) = match tool_name {
        "WebSearch" => parse_query_field(args).map(|q| {
            let t = truncate_chars(&q, 80);
            format!("Searched \"{t}\"")
        }),
        "WebFetch" => parse_string_field(args, "url").map(|u| {
            let t = truncate_chars(&u, 80);
            format!("Fetched {t}")
        }),
        "SearchTools" => parse_query_field(args).map(|q| {
            let t = truncate_chars(&q, 60);
            format!("Searched tools: \"{t}\"")
        }),
        _ => None,
    } {
        return s;
    }
    format_generic_invocation(tool_name, args)
}

fn parse_query_field(args: &str) -> Option<String> {
    parse_string_field(args, "query")
}

fn parse_string_field(args: &str, key: &str) -> Option<String> {
    let v: Value = serde_json::from_str(args).ok()?;
    v.get(key)?.as_str().map(str::to_string)
}

fn format_generic_invocation(name: &str, args: &str) -> String {
    if args.is_empty() {
        return format!("{name}()");
    }
    if let Ok(v) = serde_json::from_str::<Value>(args) {
        if let Some(obj) = v.as_object() {
            if obj.len() == 1 {
                let val = obj.values().next().unwrap();
                if let Some(s) = val.as_str() {
                    return format!("{name}(\"{s}\")");
                }
            }
        }
    }
    let compact = truncate_display_width(args, 60);
    format!("{name}({compact})")
}

/// Truncate to at most `max` Unicode scalar values; append `...` like C# `ToolDisplayHelpers.Truncate`.
fn truncate_chars(s: &str, max: usize) -> String {
    let it = s.chars();
    let count = it.clone().count();
    if count <= max {
        return s.to_string();
    }
    it.take(max).collect::<String>() + "..."
}

/// Match `chat_view::truncate`: display-width–aware truncation for raw JSON fallback.
fn truncate_display_width(s: &str, max_cols: usize) -> String {
    if max_cols == 0 {
        return String::new();
    }
    let total_width: usize = s
        .chars()
        .map(|c| UnicodeWidthChar::width(c).unwrap_or(0))
        .sum();
    if total_width <= max_cols {
        return s.to_string();
    }
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

/// Structured result lines like `ToolRegistry.FormatToolResult`; `None` means use raw text lines.
pub fn format_result_summary(tool_name: &str, result: &str) -> Option<Vec<String>> {
    let trimmed = result.trim();
    if trimmed.is_empty() {
        return None;
    }
    match tool_name {
        "WebSearch" => {
            let root: Value = serde_json::from_str(trimmed).ok()?;
            let root = peel_json_string_wrapper(root)?;
            parse_web_search_result(&root)
        }
        "WebFetch" => {
            let root: Value = serde_json::from_str(trimmed).ok()?;
            let root = peel_json_string_wrapper(root)?;
            parse_web_fetch_result(&root)
        }
        "SearchTools" => {
            let first = trimmed
                .lines()
                .find(|l| !l.trim().is_empty())
                .map(str::trim)
                .filter(|s| !s.is_empty())?;
            Some(vec![first.to_string()])
        }
        _ => None,
    }
}

fn peel_json_string_wrapper(root: Value) -> Option<Value> {
    match root {
        Value::String(s) => serde_json::from_str(&s).ok(),
        o => Some(o),
    }
}

fn parse_web_search_result(root: &Value) -> Option<Vec<String>> {
    let obj = root.as_object()?;

    if let Some(err) = obj.get("error") {
        let msg = err.as_str()?.trim();
        if msg.is_empty() {
            return None;
        }
        return Some(vec![format!("Error: {msg}")]);
    }

    let results = obj.get("results")?.as_array()?;
    let count = results.len();

    if count == 0 {
        return Some(vec!["No results found.".to_string()]);
    }

    let mut lines = Vec::new();
    lines.push(format!(
        "{} result{}:",
        count,
        if count == 1 { "" } else { "s" }
    ));

    for (idx, item) in results.iter().enumerate() {
        let title = item.get("title").and_then(|v| v.as_str());
        let url = item.get("url").and_then(|v| v.as_str());
        let title_text = truncate_chars(title.or(url).unwrap_or("?"), 70);
        let line = if let Some(u) = url {
            if let Some(domain) = host_from_url(u) {
                if domain.is_empty() {
                    format!("{}. {}", idx + 1, title_text)
                } else {
                    format!("{}. {} — {}", idx + 1, title_text, domain)
                }
            } else {
                format!("{}. {}", idx + 1, title_text)
            }
        } else {
            format!("{}. {}", idx + 1, title_text)
        };
        lines.push(line);
    }

    Some(lines)
}

fn host_from_url(url: &str) -> Option<String> {
    let rest = url
        .strip_prefix("https://")
        .or_else(|| url.strip_prefix("http://"))
        .or_else(|| url.strip_prefix("ftp://"))?;
    let host_port = rest
        .split(|c| c == '/' || c == '?' || c == '#')
        .next()?;
    let host = host_port.rsplit('@').next().unwrap_or(host_port);
    if host.is_empty() {
        None
    } else {
        Some(host.to_string())
    }
}

fn parse_web_fetch_result(root: &Value) -> Option<Vec<String>> {
    let obj = root.as_object()?;

    if let Some(err) = obj.get("error") {
        let msg = err.as_str()?.trim();
        if msg.is_empty() {
            return None;
        }
        return Some(vec![format!("Error: {msg}")]);
    }

    let mut parts: Vec<String> = Vec::new();

    if let Some(n) = obj.get("status").and_then(json_number_to_i32) {
        parts.push(n.to_string());
    }

    if let Some(len) = obj.get("length").and_then(json_number_to_i64) {
        parts.push(format!("{} chars", format_int_grouped(len)));
    }

    if let Some(ext) = obj.get("extractor").and_then(|v| v.as_str()) {
        let t = ext.trim();
        if !t.is_empty() {
            parts.push(t.to_string());
        }
    }

    if obj
        .get("truncated")
        .and_then(|v| v.as_bool())
        .unwrap_or(false)
    {
        parts.push("truncated".to_string());
    }

    if parts.is_empty() {
        None
    } else {
        Some(vec![parts.join(" · ")])
    }
}

fn json_number_to_i32(v: &Value) -> Option<i32> {
    match v {
        Value::Number(n) => n.as_i64().and_then(|x| i32::try_from(x).ok()),
        _ => None,
    }
}

fn json_number_to_i64(v: &Value) -> Option<i64> {
    match v {
        Value::Number(n) => n.as_i64(),
        _ => None,
    }
}

/// Match C# numeric format `N0` (grouped thousands, no fraction).
/// Match C# numeric format `N0` (grouped thousands, no fraction).
fn format_int_grouped(n: i64) -> String {
    let (prefix, abs_s) = if n < 0 {
        ("-", n.unsigned_abs().to_string())
    } else {
        ("", n.to_string())
    };
    let rev: Vec<char> = abs_s.chars().rev().collect();
    let mut out = String::new();
    for (i, c) in rev.iter().enumerate() {
        if i > 0 && i % 3 == 0 {
            out.push(',');
        }
        out.push(*c);
    }
    let grouped: String = out.chars().rev().collect();
    format!("{prefix}{grouped}")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn invocation_websearch_query_and_max_results() {
        let args = r#"{"query":"rust async","maxResults":5}"#;
        assert_eq!(
            format_invocation_display("WebSearch", args),
            r#"Searched "rust async""#
        );
    }

    #[test]
    fn invocation_websearch_long_query_truncated() {
        let q: String = "a".repeat(100);
        let args = format!(r#"{{"query":"{q}"}}"#);
        let out = format_invocation_display("WebSearch", &args);
        assert!(out.starts_with("Searched \""));
        assert!(out.ends_with("...\""));
    }

    #[test]
    fn invocation_webfetch_url() {
        let args = r#"{"url":"https://example.com/path"}"#;
        assert_eq!(
            format_invocation_display("WebFetch", args),
            "Fetched https://example.com/path"
        );
    }

    #[test]
    fn invocation_search_tools() {
        let args = r#"{"query":"ReadFile"}"#;
        assert_eq!(
            format_invocation_display("SearchTools", args),
            r#"Searched tools: "ReadFile""#
        );
    }

    #[test]
    fn invocation_generic_single_string_field() {
        let args = r#"{"path":"src/main.rs"}"#;
        assert_eq!(
            format_invocation_display("ReadFile", args),
            r#"ReadFile("src/main.rs")"#
        );
    }

    #[test]
    fn result_websearch_results_and_domains() {
        let json = r#"{"query":"q","provider":"exa","results":[{"title":"T","url":"https://exa.ai/docs"},{"title":"U","url":"http://b.com/x"}]}"#;
        let lines = format_result_summary("WebSearch", json).expect("lines");
        assert_eq!(lines.len(), 3);
        assert_eq!(lines[0], "2 results:");
        assert!(lines[1].contains("exa.ai"));
        assert!(lines[2].contains("b.com"));
    }

    #[test]
    fn result_websearch_error() {
        let json = r#"{"error":"rate limited"}"#;
        let lines = format_result_summary("WebSearch", json).expect("lines");
        assert_eq!(lines, vec!["Error: rate limited"]);
    }

    #[test]
    fn result_websearch_empty_results_array() {
        let json = r#"{"query":"x","results":[]}"#;
        let lines = format_result_summary("WebSearch", json).expect("lines");
        assert_eq!(lines, vec!["No results found."]);
    }

    #[test]
    fn result_websearch_double_encoded_string() {
        let inner = r#"{"results":[{"title":"Hi","url":"https://z.com"}]}"#;
        let outer = serde_json::to_string(&serde_json::Value::String(inner.to_string()))
            .expect("stringify");
        let lines = format_result_summary("WebSearch", &outer).expect("lines");
        assert_eq!(lines.len(), 2);
        assert_eq!(lines[0], "1 result:");
    }

    #[test]
    fn result_webfetch_summary() {
        let json = r#"{"status":200,"length":50000,"extractor":"readability","truncated":true}"#;
        let lines = format_result_summary("WebFetch", json).expect("lines");
        assert_eq!(lines.len(), 1);
        assert!(lines[0].contains("200"));
        assert!(lines[0].contains("50,000"));
        assert!(lines[0].contains("readability"));
        assert!(lines[0].contains("truncated"));
    }

    #[test]
    fn result_search_tools_first_line() {
        let text = "Found 3 matching tool(s)\nReadFile\nWriteFile";
        let lines = format_result_summary("SearchTools", text).expect("lines");
        assert_eq!(lines, vec!["Found 3 matching tool(s)"]);
    }

    #[test]
    fn result_unknown_tool_returns_none() {
        assert!(format_result_summary("ReadFile", "{}").is_none());
    }
}
