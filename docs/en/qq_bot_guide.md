# DotCraft QQ Bot Guide

This document describes how to configure DotCraft to connect to a QQ bot, enabling it to serve as an intelligent assistant in QQ group chats and private messages.

## Prerequisites

| Requirement | Description |
|-------------|-------------|
| A QQ account | QQ number for the bot (recommended to use an alt account) |
| NapCat | QQ protocol framework for logging into QQ |
| DotCraft | Compiled DotCraft executable |
| LLM API Key | OpenAI-compatible API Key (e.g., OpenAI, DeepSeek, etc.) |

> **Note**: Using third-party QQ protocol frameworks carries account risks (bans, etc.). Please evaluate the risks yourself.

---

## Step 1: Install NapCat

NapCat is currently the most active OneBot V11 protocol implementation, based on the NTQQ kernel.

### Windows Users (Recommended: One-Click Package)

1. Go to [NapCat Releases](https://github.com/NapNeko/NapCatQQ/releases) and download the latest version
2. Download **`NapCat.Shell.Windows.OneKey.zip`** (Windows one-click package, includes QQ, no additional installation needed)
3. Extract to any directory
4. Run `launcher.bat` to start

### Linux Users

**Method 1: One-Click Install Script**

```bash
curl -o napcat.sh https://nclatest.znin.net/NapNeko/NapCat-Installer/main/script/install.sh && sudo bash napcat.sh
```

**Method 2: Docker Deployment**

```bash
docker run -d \
  -e ACCOUNT=your_qq_number \
  -e WSR_ENABLE=true \
  -e WS_URLS='["ws://127.0.0.1:6700/"]' \
  -e WEBUI_TOKEN='your-webui-password' \
  -p 6099:6099 \
  --name napcat \
  --restart=always \
  mlikiowa/napcat-docker:latest
```

- `ACCOUNT`: Bot QQ number
- `WS_URLS`: DotCraft's WebSocket address (reverse WS)
- `6099`: NapCat WebUI management page port

### macOS Users

Go to [NapCat Releases](https://github.com/NapNeko/NapCatQQ/releases) and download the macOS DMG version.

---

## Step 2: Configure DotCraft

Add QQ bot configuration in your DotCraft workspace's `config.json`:

```json
{
    "ApiKey": "your-llm-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "QQBot": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 6700,
        "AccessToken": "",
        "AdminUsers": [123456789],
        "WhitelistedUsers": [111111111, 222222222],
        "WhitelistedGroups": [333333333],
        "ApprovalTimeoutSeconds": 60
    }
}
```

### Basic Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `QQBot.Enabled` | Enable QQ bot mode | `false` |
| `QQBot.Host` | WebSocket server listen address | `127.0.0.1` |
| `QQBot.Port` | WebSocket server listen port | `6700` |
| `QQBot.AccessToken` | Auth token (optional, must match NapCat) | empty |

> If NapCat and DotCraft run on the same machine, keep Host as `127.0.0.1`.

### Permission Configuration

| Config Item | Description | Default |
|-------------|-------------|---------|
| `QQBot.AdminUsers` | Admin QQ number list (highest permissions) | `[]` |
| `QQBot.WhitelistedUsers` | Whitelisted user QQ number list (basic permissions) | `[]` |
| `QQBot.WhitelistedGroups` | Whitelisted group number list (group members auto-get basic permissions) | `[]` |
| `QQBot.ApprovalTimeoutSeconds` | Operation approval timeout (seconds), auto-reject on timeout | `60` |

---

## Step 3: Configure NapCat to Connect to DotCraft

After NapCat starts, configure it via the WebUI in your browser:

1. Open browser and visit `http://127.0.0.1:6099`
2. Enter WebUI Token to log in
3. Scan QR code with mobile QQ to log in the bot account
4. Go to **Network Configuration** page
5. Click **New** -> Select **WebSocket Client** (reverse WS)
6. Fill in configuration:
   - **URL**: `ws://127.0.0.1:6700/` (matching DotCraft's Host:Port)
   - **Token**: If DotCraft has AccessToken configured, enter the same value here
   - **Message Format**: Select `array` (**required**, do not select `string`)
7. Click **Save**

> If deploying NapCat via Docker, replace `127.0.0.1` in the URL with the IP of the machine running DotCraft or the Docker network address.

---

## Step 4: Start DotCraft

```bash
./DotCraft
```

After starting, you will see output like:

```
QQ Bot mode enabled
OneBot reverse WebSocket server started on ws://127.0.0.1:6700/
QQ Bot listening on ws://127.0.0.1:6700/
Press Ctrl+C to stop...
```

When NapCat successfully connects, you will see:

```
[QQ] Client connected: <connection-id> from <ip:port>
[QQ] OneBot lifecycle event: sub_type=connect
```

---

## Usage

### Group Chat Mode

- The bot **only replies to messages that @mention it**
- In group chat, `@bot hello` will trigger a reply
- Unauthorized users @mentioning the bot will be silently ignored

### Private Chat Mode

- The bot **only replies to authorized users'** private messages
- Unauthorized users' private messages will be ignored

### Session Management

- Each QQ session (group number or private chat user) has independent context
- Session data is automatically saved in the DotCraft workspace's `sessions/` directory

---

## Permissions & Security

### User Roles

DotCraft classifies QQ users into three roles:

| Role | Source | Permission Scope |
|------|--------|------------------|
| **Admin** | QQ numbers in `AdminUsers` list | Highest permissions, can execute write operations (requires approval) |
| **Whitelisted** | QQ numbers in `WhitelistedUsers` list, or members of groups in `WhitelistedGroups` list | Basic permissions, can chat and read files |
| **Unauthorized** | Not in any of the above lists | Bot does not respond |

### Operation Tiers

Different roles can perform different operation levels:

| Operation Tier | Operations | Whitelisted | Admin |
|----------------|-----------|-------------|-------|
| Tier 0 | Pure conversation (no tool calls) | Allowed | Allowed |
| Tier 1 | Read workspace files, Web requests | Allowed | Allowed |
| Tier 2 | Write workspace files, execute Shell commands | Rejected | Requires approval |
| Tier 3 | Write files outside workspace, execute commands outside workspace | Rejected | Rejected |

### Shell Command Security Rules

The bot performs **cross-platform path static analysis** on Shell commands, detecting the following path forms:
- **Unix**: Absolute paths (`/etc`), home directory (`~/.ssh`), environment variables (`$HOME`, `${HOME}`)
- **Windows**: Drive paths (`C:\`, `D:\Users`), environment variables (`%USERPROFILE%`, `%APPDATA%`), UNC paths (`\\server\share`)

Trigger rules:
- When `Tools.Shell.RequireApprovalOutsideWorkspace = false`: Such commands are directly rejected
- When `Tools.Shell.RequireApprovalOutsideWorkspace = true`: Only admins can execute after approval, other roles are directly rejected
- Blacklisted paths (e.g., `~/.ssh`, `/etc/shadow`) are always rejected (regardless of approval)

Examples:
- `ls /etc`, `dir C:\`, `cat ~/.ssh/id_rsa`, `type %USERPROFILE%\secret.txt` -> Triggers detection
- `ls ./src`, `echo test > NUL` -> Does not trigger (within workspace / safe device whitelist)

### Operation Approval Flow

When an admin triggers a Tier 2 operation, DotCraft requests approval via QQ message:

1. The bot sends an approval request in the current session (in group chat, it @mentions the admin):
   ```
   Warning: Operation Approval Request
   File operation: write
   Path: ./src/main.cs

   Please reply: approve / reject (auto-reject after 60s timeout)
   ```
2. The admin replies with any of the following keywords to complete the approval:
   - **Approve**: `approve`, `yes`, `y`
   - **Reject**: `reject`, `no`, `n`, `deny`
3. Auto-reject on timeout (default 60 seconds, adjustable via `ApprovalTimeoutSeconds`)

### Configuration Examples

**Minimum permissions** (recommended): Only allow specific admin to operate

```json
{
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    }
}
```

**Group public config**: Allow all group members to use basic features

```json
{
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789],
        "WhitelistedGroups": [987654321, 111222333]
    }
}
```

**Mixed config**: Specified group whitelist + additional user whitelist

```json
{
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789],
        "WhitelistedUsers": [111111111, 222222222],
        "WhitelistedGroups": [333333333],
        "ApprovalTimeoutSeconds": 120
    }
}
```

> **Note**: If `AdminUsers`, `WhitelistedUsers`, and `WhitelistedGroups` are all empty, the bot will not respond to anyone. Please configure at least one admin.

---

## Complete Deployment Example

Here is a complete deployment flow on a Linux server:

```bash
# 1. Install NapCat (Docker method)
docker run -d \
  -e ACCOUNT=123456789 \
  -e WSR_ENABLE=true \
  -e WS_URLS='["ws://host.docker.internal:6700/"]' \
  -e WEBUI_TOKEN='my-secret-token' \
  -p 6099:6099 \
  --name napcat \
  --restart=always \
  --add-host=host.docker.internal:host-gateway \
  mlikiowa/napcat-docker:latest

# 2. Visit http://server-ip:6099 and scan QR code to log in QQ

# 3. Configure DotCraft (ensure QQBot.Enabled = true in config.json)

# 4. Start DotCraft
./DotCraft
```

---

## Configuration Templates

### Minimal Configuration

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    }
}
```

### Full Configuration

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "QQBot": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 6700,
        "AccessToken": "your-optional-token",
        "AdminUsers": [123456789],
        "WhitelistedUsers": [111111111, 222222222],
        "WhitelistedGroups": [333333333, 444444444],
        "ApprovalTimeoutSeconds": 60
    },
    "Security": {
        "BlacklistedPaths": [
            "~/.ssh",
            "/etc/shadow"
        ]
    }
}
```
