use anyhow::{anyhow, bail, Result};
use serde::Deserialize;
use serde_json::json;
use std::{
    path::PathBuf,
    process::{Command, Stdio},
    time::{Duration, Instant},
};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::TcpStream,
    time,
};

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct HubLockInfo {
    api_base_url: String,
    token: String,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct HubAppServerResponse {
    pub endpoints: std::collections::HashMap<String, String>,
}

#[derive(Debug, Deserialize)]
struct HubErrorResponse {
    error: HubError,
}

#[derive(Debug, Deserialize)]
struct HubError {
    code: String,
    message: String,
}

const STARTUP_TIMEOUT: Duration = Duration::from_secs(15);
const POLL_INTERVAL: Duration = Duration::from_millis(200);

pub async fn ensure_appserver(
    workspace_path: &std::path::Path,
    dotcraft_bin: &str,
) -> Result<String> {
    let hub = ensure_hub(dotcraft_bin).await?;
    let body = json!({
        "workspacePath": workspace_path,
        "client": {
            "name": "dotcraft-tui",
            "version": env!("CARGO_PKG_VERSION")
        },
        "startIfMissing": true
    });
    let response: HubAppServerResponse = hub_request_json(
        &hub,
        "POST",
        "/v1/appservers/ensure",
        Some(body.to_string()),
    )
    .await?;

    response
        .endpoints
        .get("appServerWebSocket")
        .filter(|url| !url.trim().is_empty())
        .cloned()
        .ok_or_else(|| anyhow!("Hub did not return an AppServer WebSocket endpoint"))
}

async fn ensure_hub(dotcraft_bin: &str) -> Result<HubLockInfo> {
    if let Some(hub) = try_live_hub().await {
        return Ok(hub);
    }

    Command::new(dotcraft_bin)
        .arg("hub")
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|e| anyhow!("DotCraft Hub failed to start: {e}"))?;

    let deadline = Instant::now() + STARTUP_TIMEOUT;
    while Instant::now() < deadline {
        if let Some(hub) = try_live_hub().await {
            return Ok(hub);
        }
        time::sleep(POLL_INTERVAL).await;
    }

    bail!("DotCraft Hub could not be started")
}

async fn try_live_hub() -> Option<HubLockInfo> {
    let hub = read_hub_lock()?;
    let status = hub_request_raw(&hub, "GET", "/v1/status", None).await.ok()?;
    if status.status_code == 200 {
        Some(hub)
    } else {
        None
    }
}

fn read_hub_lock() -> Option<HubLockInfo> {
    let path = hub_lock_path()?;
    let content = std::fs::read_to_string(path).ok()?;
    serde_json::from_str(&content).ok()
}

fn hub_lock_path() -> Option<PathBuf> {
    dirs::home_dir().map(|home| home.join(".craft").join("hub").join("hub.lock"))
}

async fn hub_request_json<T: serde::de::DeserializeOwned>(
    hub: &HubLockInfo,
    method: &str,
    path: &str,
    body: Option<String>,
) -> Result<T> {
    let response = hub_request_raw(hub, method, path, body).await?;
    if (200..300).contains(&response.status_code) {
        return Ok(serde_json::from_str(&response.body)?);
    }

    if let Ok(error) = serde_json::from_str::<HubErrorResponse>(&response.body) {
        bail!("Hub {}: {}", error.error.code, error.error.message);
    }

    bail!("Hub request failed with HTTP {}", response.status_code)
}

struct HttpResponse {
    status_code: u16,
    body: String,
}

async fn hub_request_raw(
    hub: &HubLockInfo,
    method: &str,
    path: &str,
    body: Option<String>,
) -> Result<HttpResponse> {
    let (host, port) = parse_http_loopback(&hub.api_base_url)?;
    let mut stream = TcpStream::connect((host.as_str(), port)).await?;
    let body = body.unwrap_or_default();
    let request = format!(
        "{method} {path} HTTP/1.1\r\nHost: {host}:{port}\r\nAuthorization: Bearer {}\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",
        hub.token,
        body.as_bytes().len(),
        body
    );
    stream.write_all(request.as_bytes()).await?;
    stream.shutdown().await?;

    let mut bytes = Vec::new();
    stream.read_to_end(&mut bytes).await?;
    let text = String::from_utf8_lossy(&bytes);
    let (head, body) = text
        .split_once("\r\n\r\n")
        .ok_or_else(|| anyhow!("Invalid Hub HTTP response"))?;
    let status_code = head
        .lines()
        .next()
        .and_then(|line| line.split_whitespace().nth(1))
        .and_then(|code| code.parse::<u16>().ok())
        .ok_or_else(|| anyhow!("Invalid Hub HTTP status"))?;
    let is_chunked = head.lines().any(|line| {
        line.to_ascii_lowercase()
            .starts_with("transfer-encoding:")
            && line.to_ascii_lowercase().contains("chunked")
    });
    let body = if is_chunked {
        decode_chunked_body(body)?
    } else {
        body.to_string()
    };

    Ok(HttpResponse {
        status_code,
        body,
    })
}

fn decode_chunked_body(body: &str) -> Result<String> {
    let mut rest = body.as_bytes();
    let mut decoded = Vec::new();
    loop {
        let header_end = rest
            .windows(2)
            .position(|w| w == b"\r\n")
            .ok_or_else(|| anyhow!("Invalid chunked Hub response"))?;
        let size_line = std::str::from_utf8(&rest[..header_end])?;
        let after_size = &rest[header_end + 2..];
        let size_hex = size_line
            .split(';')
            .next()
            .unwrap_or(size_line)
            .trim();
        let size = usize::from_str_radix(size_hex, 16)?;
        if size == 0 {
            return Ok(String::from_utf8(decoded)?);
        }
        if after_size.len() < size + 2 {
            bail!("Invalid chunked Hub response body");
        }
        decoded.extend_from_slice(&after_size[..size]);
        rest = &after_size[size..];
        if !rest.starts_with(b"\r\n") {
            bail!("Invalid chunked Hub response delimiter");
        }
        rest = &rest[2..];
    }
}

fn parse_http_loopback(url: &str) -> Result<(String, u16)> {
    let rest = url
        .strip_prefix("http://")
        .ok_or_else(|| anyhow!("Hub URL must use http://"))?;
    let host_port = rest.split('/').next().unwrap_or(rest);
    let (host, port) = host_port
        .rsplit_once(':')
        .ok_or_else(|| anyhow!("Hub URL is missing a port"))?;
    let port = port.parse::<u16>()?;
    if host != "127.0.0.1" && host != "localhost" {
        bail!("Hub URL must be loopback");
    }
    Ok((host.to_string(), port))
}

#[cfg(test)]
mod tests {
    use super::decode_chunked_body;

    #[test]
    fn decodes_chunked_body() {
        let decoded = decode_chunked_body("5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n").unwrap();
        assert_eq!(decoded, "hello world");
    }
}
