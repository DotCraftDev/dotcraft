# DotCraft WeCom Guide

DotCraft currently provides WeCom integration through the `WeComBot` config section (message receiving, session handling, and in-channel delivery).

---

## Table of Contents

- [1. WeCom Bot Mode](#1-wecom-bot-mode)
- [2. Heartbeat & Cron Integration](#2-heartbeat--cron-integration)
- [3. Full Configuration Examples](#3-full-configuration-examples)
- [4. Deployment Guide](#4-deployment-guide)

---

## 1. WeCom Bot Mode

### 1.1 Overview

WeCom Bot mode is one of DotCraft's four runtime modes (CLI / QQ Bot / WeCom Bot / API). When enabled, DotCraft runs as an HTTP server receiving WeCom message callbacks and auto-replying via the AI Agent.

Core features:
- Supports text, image, voice, file, attachment, and mixed media message types
- Voice messages auto-transcribed to text and processed as text input by AI Agent
- AES-256-CBC + SHA1 message encryption/decryption (supports variable-length EncodingAESKey)
- Supports both XML (Message Push API) and JSON (Smart Bot API) callback formats
- Multi-bot instances (path-based routing)
- Independent session management (isolated by user+session)
- Streaming response real-time push
- Integrated Heartbeat and Cron services

### 1.2 Create an Application Bot in WeCom

1. Log in to [WeCom Admin Console](https://work.weixin.qq.com/)
2. Go to **Apps & Mini Programs** > **Self-built** > **Create App**
3. Select **Group Bot** type
4. Configure **Receive Messages**:
   - URL: `http://your-server:9000/your-bot-path` (must be publicly accessible)
   - Token: Custom or WeCom-generated
   - EncodingAESKey: Custom or WeCom-generated (usually 43 characters, but supports variable length)
5. Click **Save**, the system will verify the URL (DotCraft must already be running)

### 1.3 Configuration

Add the `WeComBot` config section in `config.json`:

```json
{
    "WeComBot": {
        "Enabled": true,
        "Port": 9000,
        "Host": "0.0.0.0",
        "AdminUsers": ["zhangsan", "lisi"],
        "WhitelistedUsers": ["wangwu"],
        "WhitelistedChats": ["wrxxxxxxxx"],
        "ApprovalTimeoutSeconds": 60,
        "Robots": [
            {
                "Path": "/dotcraft",
                "Token": "your_token_here",
                "AesKey": "your_43_char_aeskey"
            }
        ],
        "DefaultRobot": {
            "Token": "default_token",
            "AesKey": "default_aeskey"
        }
    }
}
```

#### WeComBot Configuration

| Config Item | Type | Default | Description |
|-------------|------|---------|-------------|
| `Enabled` | bool | `false` | Enable WeCom Bot mode |
| `Host` | string | `"0.0.0.0"` | HTTP service listen address |
| `Port` | int | `9000` | HTTP service listen port |
| `AdminUsers` | array | `[]` | Admin user ID list (WeCom UserId) |
| `WhitelistedUsers` | array | `[]` | Whitelisted user ID list (WeCom UserId) |
| `WhitelistedChats` | array | `[]` | Whitelisted chat ID list (WeCom ChatId) |
| `ApprovalTimeoutSeconds` | int | `60` | Operation approval timeout (seconds) |
| `Robots` | array | `[]` | Bot configuration list |
| `DefaultRobot` | object | `null` | Default bot (for unmatched paths) |

#### Single Bot Configuration

| Config Item | Type | Required | Description |
|-------------|------|----------|-------------|
| `Path` | string | Yes | Bot path (e.g., `/dotcraft`) |
| `Token` | string | Yes | Token provided by WeCom |
| `AesKey` | string | Yes | EncodingAESKey (usually 43 chars, supports variable length, no equals sign) |

#### Runtime Mode Priority

| Condition | Runtime Mode |
|-----------|-------------|
| `QQBot.Enabled = true` | QQ Bot mode |
| `WeComBot.Enabled = true` | WeCom Bot mode |
| `Api.Enabled = true` | API mode |
| Other | CLI mode |

> **Note**: By default (`Gateway.Enabled = false`), these modes are mutually exclusive and only the highest-priority enabled mode runs. To run QQ Bot, WeCom Bot, and API simultaneously, enable [Gateway mode](./config_guide.md#gateway-multi-channel-concurrent-mode).

### 1.4 Permissions & Approval

WeCom Bot mode implements permission tiers and in-chat approval similar to QQ Bot.

#### Permission Tiers

User roles are divided into three levels:

| Role | Permissions | Config Item |
|------|------------|-------------|
| **Admin** | Full permissions (workspace write operations require approval) | `AdminUsers` |
| **Whitelisted** | Can perform read operations (file reading, Web search, etc.) | `WhitelistedUsers` or `WhitelistedChats` |
| **Unauthorized** | Cannot use Agent features | Not in any list |

Operation tiers:

| Tier | Description | Admin | Whitelisted | Unauthorized |
|------|-------------|-------|-------------|--------------|
| Tier 0 (Chat) | Conversation | Yes | Yes | No |
| Tier 1 (ReadOnly) | Read files/Web | Yes | Yes | No |
| Tier 2 (WriteWorkspace) | Write within workspace/commands | Yes (approval required) | No | No |
| Tier 3 (WriteOutsideWorkspace) | Write outside workspace/commands | Yes (approval required) | No | No |

#### Configuration Example

```json
{
    "WeComBot": {
        "AdminUsers": ["zhangsan", "lisi"],
        "WhitelistedUsers": ["wangwu"],
        "WhitelistedChats": ["wrxxxxxxxx"],
        "ApprovalTimeoutSeconds": 60
    }
}
```

**Notes**:
- `AdminUsers`: WeCom UserId (string format, assigned by admin)
- `WhitelistedUsers`: Whitelisted user UserId list
- `WhitelistedChats`: Whitelisted chat ChatId list; all users in that chat automatically get whitelisted permissions
- `ApprovalTimeoutSeconds`: Approval timeout (default 60 seconds)

> **How to get UserId**: Log in to WeCom Admin Console -> Contacts -> Member Details -> Account (this is the UserId)

#### Approval Flow

When admins perform sensitive operations (workspace write, Shell commands):

1. Agent pauses and sends an approval request in the WeCom chat (Markdown format):

```
Warning: Operation Approval Request
File operation: write
Path: /path/to/file.txt

Please reply: approve / approve all / reject (auto-reject after 60s timeout)
(approve all: skip future approval for similar operations in this session)
```

2. User replies with keywords:
   - **"approve"** / **"allow"** / **"yes"** / **"y"** -> Approve this operation
   - **"approve all"** / **"allow all"** / **"yes all"** -> Approve this operation and skip future similar approvals in this session
   - **"reject"** / **"no"** / **"n"** / **"deny"** -> Reject operation
   - No reply or timeout -> Auto-reject

3. Agent continues or aborts based on the approval result

#### Session-Level Approval Cache

When a user selects "approve all", similar operations in that session will no longer trigger approval:

- File operations are cached by type (e.g., `file:write`)
- Shell commands are cached with wildcard (`shell:*`)
- Cache is only valid for the current session
- Using `/new` to clear the session also clears the approval cache

#### Security Recommendations

1. **Principle of least privilege**: Only add necessary users to `AdminUsers`; others go to `WhitelistedUsers`
2. **Careful approval**: Read operation details in approval requests carefully; reject suspicious operations
3. **Regular review**: Periodically check `AdminUsers` and `WhitelistedUsers` lists; remove departed or no-longer-needed users
4. **Outside workspace operations**: Default config blocks outside-workspace operations with approval; keep `Tools.File.RequireApprovalOutsideWorkspace = true`

---

### 1.5 Start & Verify

```bash
cd /your/workspace
dotcraft
```

Console output on successful start:

```
[WeCom] Registered handler for: /dotcraft
Heartbeat started (interval: 1800s)
Cron service started (0 jobs)
WeCom Bot listening on http://0.0.0.0:9000
Registered bots:
  - http://0.0.0.0:9000/dotcraft
Press Ctrl+C to stop...
```

### 1.6 Command System

WeCom Bot mode supports the following slash commands:

| Command | Description |
|---------|-------------|
| `/new` | Clear current session, start new conversation |
| `/help` | Show available commands |
| `/heartbeat trigger` | Manually trigger a heartbeat check |
| `/cron list` | View all scheduled tasks |
| `/cron remove <id>` | Delete a scheduled task |

### 1.7 Session Management

Each user's session is independently stored, following the naming convention:

```
sessionId = "wecom_{chatId}_{userId}"
```

- Different users in the same group chat have independent sessions
- The same user in different group chats has independent sessions
- All sessions share long-term memory (`MEMORY.md`)

Session files are stored in the `.craft/sessions/` directory.

### 1.8 Push Capabilities (IWeComPusher)

The message pusher in WeCom Bot mode supports multiple message formats:

| Method | Description |
|--------|-------------|
| `PushTextAsync` | Text message, supports @mention and visibility control |
| `PushMarkdownAsync` | Markdown message |
| `PushImageAsync` | Image message (pass byte array) |
| `PushNewsAsync` | News/article message (max 8 articles) |
| `PushMiniProgramAsync` | Mini program card |
| `PushVoiceAsync` | Voice message (requires uploading media first to get media_id) |
| `PushFileAsync` | File message (requires uploading media first to get media_id) |
| `UploadMediaAsync` | Upload temporary media, returns media_id |
| `PushRawAsync` | Raw JSON |

Example:

```csharp
await pusher.PushTextAsync("Hello World");

await pusher.PushTextAsync("Important notice",
    mentionedList: new List<string> { "@all" });

await pusher.PushTextAsync("Confidential info",
    visibleToUser: new List<string> { "userid1" });

await pusher.PushMarkdownAsync(
    "# Title\n- List item 1\n- List item 2");

await pusher.PushNewsAsync(new List<WeComArticle>
{
    new() {
        Title = "Title",
        Description = "Description",
        Url = "https://example.com",
        PicUrl = "https://example.com/pic.jpg"
    }
});

// Upload and send voice
using var voiceStream = File.OpenRead("voice.amr");
var mediaId = await pusher.UploadMediaAsync(voiceStream, "voice.amr", "voice");
await pusher.PushVoiceAsync(mediaId);

// Upload and send file
using var fileStream = File.OpenRead("report.pdf");
var fileMediaId = await pusher.UploadMediaAsync(fileStream, "report.pdf", "file");
await pusher.PushFileAsync(fileMediaId);
```

> **Note**: The sending capabilities in this guide are all based on WeCom Bot mode (`WeComBot`) and its `IWeComPusher`.
>
> **Voice/File Limits**: Uploaded temporary media expires after 3 days. Voice supports AMR format. For file size limits, refer to the official WeCom documentation.

### 1.9 Voice & File Messages

The WeCom Smart Bot API supports receiving voice and file messages in single chats. DotCraft handles these two message types as follows:

#### Voice Messages (Receiving)

WeCom automatically converts voice messages to text (ASR). DotCraft receives the transcribed text and passes it to the AI Agent as text input. This means sending a voice message is equivalent to sending a text message, and the Agent will respond normally.

Message flow:

```
User sends voice -> WeCom ASR transcription -> DotCraft receives voice.content
  -> Routes to TextHandler -> AI Agent processes -> Replies with text
```

#### File Messages (Receiving)

When a user sends a file in a single chat, DotCraft receives the file's encrypted download URL and logs it in the common message handler. The default behavior is to log the file URL and reply with a confirmation message.

#### Voice/File Messages (Sending)

The active runtime model is:
- Agent conversations use channel-native runtime tools such as `WeComSendVoice` and `WeComSendFile`.
- Custom bot development can still use `IWeComPusher` directly when you need lower-level control.

`IWeComPusher` provides `PushVoiceAsync` and `PushFileAsync` methods for sending voice and file messages. Before sending, you need to upload temporary media via `UploadMediaAsync` to get the `media_id`.

> **Current Runtime Behavior**: `WeComSendVoice` / `WeComSendFile` are current-chat runtime tools. They only work in WeCom Bot mode, require active chat context from the current message, and send back into that same WeCom chat.

Custom handler example:

```csharp
// Send voice in custom handler
using var voiceStream = File.OpenRead("voice.amr");
var mediaId = await pusher.UploadMediaAsync(voiceStream, "voice.amr", "voice");
await pusher.PushVoiceAsync(mediaId);

// Send file in custom handler
using var fileStream = File.OpenRead("report.pdf");
var fileMediaId = await pusher.UploadMediaAsync(fileStream, "report.pdf", "file");
await pusher.PushFileAsync(fileMediaId);
```

#### Limitations & Notes

| Item | Description |
|------|-------------|
| Chat type | Voice and file messages only supported in single chats, not group chats |
| Callback format | Voice/file via Smart Bot API (JSON format), not the legacy XML Push API |
| Media expiry | Uploaded temporary media expires after 3 days |
| Voice format | Only AMR format supported |
| Voice-to-text | Done by WeCom server-side; DotCraft directly receives the transcription |

> Reference: [WeCom Smart Bot API Documentation](https://developer.work.weixin.qq.com/document/path/100719)

### 1.10 Agent Built-in Tools (WeCom Mode)

In WeCom Bot mode, the Agent can directly call the following channel-native runtime tools to send content to the current WeCom chat:

| Tool Name | Function | Notes |
|-----------|----------|-------|
| `WeComSendVoice(filePath)` | Send voice (AMR recommended) | Current-chat only; local absolute path only; uploads media first |
| `WeComSendFile(filePath)` | Send file | Current-chat only; local absolute path only; uploads media first |

> Note: These tools work from the current message context's chatId and do not expose arbitrary cross-target delivery. If you need lower-level or custom delivery behavior, keep using `IWeComPusher` inside custom handlers.

---

## 2. Heartbeat & Cron Integration

### Heartbeat Notification

When `Heartbeat.NotifyAdmin` is enabled and a WeCom bot session has been established, heartbeat results are pushed through the available webhook cached for that session target.

```json
{
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": true
    },
    "WeComBot": {
        "Enabled": true,
        "Host": "0.0.0.0",
        "Port": 9000,
        "Robots": [
            {
                "Path": "/dotcraft",
                "Token": "your_token",
                "AesKey": "your_43_char_aeskey"
            }
        ]
    }
}
```

- **QQ Bot mode**: Heartbeat results are sent to QQ admin private chat
- **WeCom Bot mode**: Heartbeat results are sent to WeCom bot chats (requires cached webhook by chatId)

### Cron Delivery Channels

| `channel` | `to` | Delivery Target | Prerequisite |
|-----------|------|----------------|--------------|
| `"qq"` | `"group:<groupId>"` | QQ group | QQ Bot mode |
| `"qq"` | `"<qqUserId>"` | QQ private chat | QQ Bot mode |
| `"wecom"` | `"<ChatId>"` | Specific WeCom group | WeCom Bot mode (chatId has webhook cache) |
| `"wecom"` | (omit) | Not recommended / likely to fail | WeCom Bot delivery requires resolvable target |

> Create Cron tasks from inside a WeCom group chat whenever possible. This captures the current ChatId automatically. If the target chatId has no webhook cache yet, delivery fails with `No WeCom webhook is available for target ...`.

---

## 3. Full Configuration Examples

### WeCom Bot Only

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "WeComBot": {
        "Enabled": true,
        "Host": "0.0.0.0",
        "Port": 9000,
        "Robots": [
            {
                "Path": "/dotcraft",
                "Token": "your_token",
                "AesKey": "your_43_char_aeskey"
            }
        ]
    }
}
```

### Enable WeCom Bot Mode

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1",
    "WeComBot": {
        "Enabled": true,
        "Port": 9000,
        "Host": "0.0.0.0",
        "AdminUsers": ["zhangsan", "lisi"],
        "WhitelistedUsers": ["wangwu"],
        "WhitelistedChats": ["wrxxxxxxxx"],
        "ApprovalTimeoutSeconds": 60,
        "Robots": [
            {
                "Path": "/dotcraft",
                "Token": "your_token",
                "AesKey": "your_43_char_aeskey"
            }
        ]
    },
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": true
    },
    "Cron": {
        "Enabled": true,
        "StorePath": "cron/jobs.json"
    }
}
```

### Enable Both QQ Bot and WeCom Bot (Gateway)

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "Gateway": {
        "Enabled": true
    },
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    },
    "WeComBot": {
        "Enabled": true,
        "Port": 9000,
        "Host": "0.0.0.0",
        "Robots": [
            {
                "Path": "/dotcraft",
                "Token": "your_token",
                "AesKey": "your_43_char_aeskey"
            }
        ]
    },
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": true
    }
}
```

---

## 4. Deployment Guide

Please follow the official WeChat Work documentation for deployment: [WeChat Work - Intelligent Robot](https://developer.work.weixin.qq.com/document/path/101039)

---

## References

- [Enterprise WeChat Group Robot Configuration Instructions](https://developer.work.weixin.qq.com/document/path/99110)

- [Enterprise WeChat Smart Robot Interface Documentation](https://developer.work.weixin.qq.com/document/path/100719)

- [DotCraft Configuration Guide](./config_guide.md)
