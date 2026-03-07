# DotCraft 企业微信指南

DotCraft 提供两种企业微信集成能力：

| 能力 | 说明 | 配置节 |
|------|------|--------|
| **企业微信推送** | 通过群机器人 Webhook 向企业微信群发送通知 | `WeCom` |
| **企业微信机器人** | 作为独立运行模式，接收并响应企业微信消息 | `WeComBot` |

两者可独立使用，也可同时启用。

---

## 目录

- [一、企业微信推送](#一企业微信推送)
- [二、企业微信机器人模式](#二企业微信机器人模式)
- [三、心跳与定时任务集成](#三心跳与定时任务集成)
- [四、完整配置示例](#四完整配置示例)
- [五、部署指南](#五部署指南)

---

## 一、企业微信推送

### 1.1 概述

企业微信推送基于**群机器人 Webhook**，是一种单向通知机制。DotCraft 通过 HTTPS POST 将消息投递到企业微信群，无需额外登录或客户端。

适用场景：
- Agent 执行任务后自动通知群组
- Heartbeat 巡检结果推送
- Cron 定时任务结果投递

### 1.2 创建群机器人

1. 打开企业微信客户端，进入目标群聊
2. 点击右上角 **···** > **群设置** > **群机器人** > **添加机器人**
3. 输入机器人名称（例如 DotCraft 通知），点击 **添加**
4. 复制生成的 Webhook 地址

> **安全提示**：Webhook URL 中的 `key` 是唯一凭证，任何获得该 URL 的人都可以向群发送消息。请勿将其提交到公共代码仓库。

### 1.3 配置

在 `config.json` 中添加 `WeCom` 配置节：

```json
{
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
    }
}
```

| 配置项 | 类型 | 默认值 | 必填 | 说明 |
|--------|------|--------|------|------|
| `WeCom.Enabled` | bool | `false` | 是 | 是否启用企业微信推送 |
| `WeCom.WebhookUrl` | string | 空 | 是 | 完整的 Webhook URL（含 key） |

**推荐做法**：将 `WeCom.WebhookUrl` 配置在全局配置（`~/.craft/config.json`）而非工作区配置，避免将敏感信息提交到 Git 仓库。

### 1.4 使用方式

#### Agent 智能调用

启用后，Agent 会自动获得 `WeComNotify` 工具，可根据上下文智能决定何时发送通知。

工具参数：
- `message`（必填）：通知消息内容（最长 2048 字节，UTF-8）
- `mentionList`（可选）：@成员的 userid 列表，逗号分隔（如 `"userid1,userid2"`），或 `"@all"` 表示 @所有人

#### Heartbeat 自动通知

当 `Heartbeat.NotifyAdmin = true` 时，心跳执行结果会自动推送到企业微信群。详见[心跳与定时任务集成](#三心跳与定时任务集成)。

#### Cron 定时投递

Cron 任务支持投递到企业微信群，只需在创建任务时指定 `channel: "wecom"`。

### 1.5 消息格式与限制

| 项目 | 限制 |
|------|------|
| 消息格式 | 纯文本（`text` 类型），支持换行符 |
| 最大长度 | 2048 字节（UTF-8） |
| 频率限制 | 每个群机器人 20 条/分钟 |

> **注意**：`userid` 是企业微信的成员账号（由管理员分配），不是手机号或 QQ 号。

---

## 二、企业微信机器人模式

### 2.1 概述

企业微信机器人模式是 DotCraft 的三种运行模式之一（CLI / QQ Bot / WeCom Bot）。启用后，DotCraft 作为 HTTP 服务器接收企业微信的消息回调，并通过 AI Agent 自动回复。

核心特性：
- 支持文本、图片、语音、文件、附件、图文混排等消息类型
- 语音消息自动转文本，作为文本输入交给 AI Agent 处理
- AES-256-CBC + SHA1 消息加解密（支持可变长度 EncodingAESKey）
- 同时支持 XML（消息推送 API）和 JSON（智能机器人 API）两种回调格式
- 多机器人实例（基于路径路由）
- 独立会话管理（按用户+会话隔离）
- 流式响应实时推送
- 集成 Heartbeat 和 Cron 服务

### 2.2 在企业微信中创建应用机器人

1. 登录[企业微信管理后台](https://work.weixin.qq.com/)
2. 进入 **应用与小程序** > **自建** > **创建应用**
3. 选择 **群机器人** 类型
4. 配置 **接收消息**：
   - URL：`http://your-server:9000/your-bot-path`（需公网可访问）
   - Token：自定义或由企业微信生成
   - EncodingAESKey：自定义或由企业微信生成（通常为 43 位，但支持可变长度）
5. 点击 **保存**，系统会验证 URL（DotCraft 必须已启动）

### 2.3 配置

在 `config.json` 中添加 `WeComBot` 配置节：

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

#### WeComBot 配置项

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `Enabled` | bool | `false` | 是否启用企业微信机器人模式 |
| `Host` | string | `"0.0.0.0"` | HTTP 服务监听地址 |
| `Port` | int | `9000` | HTTP 服务监听端口 |
| `AdminUsers` | array | `[]` | 管理员用户 ID 列表（企业微信 UserId） |
| `WhitelistedUsers` | array | `[]` | 白名单用户 ID 列表（企业微信 UserId） |
| `WhitelistedChats` | array | `[]` | 白名单会话 ID 列表（企业微信 ChatId） |
| `ApprovalTimeoutSeconds` | int | `60` | 操作审批超时（秒） |
| `Robots` | array | `[]` | 机器人配置列表 |
| `DefaultRobot` | object | `null` | 默认机器人（用于未匹配的路径） |

#### 单个机器人配置

| 配置项 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `Path` | string | 是 | 机器人路径（如 `/dotcraft`） |
| `Token` | string | 是 | 企业微信提供的 Token |
| `AesKey` | string | 是 | EncodingAESKey（通常 43 位，支持可变长度，不含等号） |

#### 运行模式优先级

| 条件 | 运行模式 |
|------|----------|
| `QQBot.Enabled = true` | QQ 机器人模式 |
| `WeComBot.Enabled = true` | 企业微信机器人模式 |
| `Api.Enabled = true` | API 模式 |
| 其他 | CLI 模式 |

> **注意**：默认情况下（`Gateway.Enabled = false`），以上模式按优先级顺序互斥，仅运行优先级最高的一个。若需同时运行 QQ Bot、WeCom Bot 和 API，请启用 [Gateway 模式](./config_guide.md#gateway-多-channel-并发模式)。

### 2.4 权限与审批机制

企业微信机器人模式实现了与 QQ Bot 类似的权限分级和聊天内审批机制。

#### 权限分级

用户角色分为三级：

| 角色 | 权限 | 配置项 |
|------|------|--------|
| **Admin** | 管理员，拥有所有权限（工作区内写入需审批） | `AdminUsers` |
| **Whitelisted** | 白名单用户，可执行读取操作（文件读取、Web 搜索等） | `WhitelistedUsers` 或 `WhitelistedChats` |
| **Unauthorized** | 未授权，无法使用 Agent 功能 | 不在任何列表中 |

操作分级：

| 级别 | 说明 | Admin | Whitelisted | Unauthorized |
|------|------|-------|-------------|--------------|
| Tier 0（Chat） | 对话 | ✅ | ✅ | ❌ |
| Tier 1（ReadOnly） | 读文件/Web | ✅ | ✅ | ❌ |
| Tier 2（WriteWorkspace） | 工作区内写入/命令 | ✅ (需审批) | ❌ | ❌ |
| Tier 3（WriteOutsideWorkspace） | 工作区外写入/命令 | ✅ (需审批) | ❌ | ❌ |

#### 配置示例

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

**说明**：
- `AdminUsers`：企业微信 UserId（字符串格式，由管理员分配）
- `WhitelistedUsers`：白名单用户 UserId 列表
- `WhitelistedChats`：白名单会话 ChatId 列表，该会话中的所有用户自动获得白名单权限
- `ApprovalTimeoutSeconds`：审批超时时间（默认 60 秒）

> **如何获取 UserId**：登录企业微信管理后台 → 通讯录 → 成员详情 → 账号（即为 UserId）

#### 审批流程

当管理员执行敏感操作（工作区内/外写入、Shell 命令）时：

1. Agent 暂停并在企业微信会话中发送审批请求（Markdown 格式）：

```
⚠️ 操作审批请求
文件操作: write
路径: /path/to/file.txt

请回复: 同意 / 同意全部 / 拒绝 (超时60秒自动拒绝)
(同意全部: 本会话中不再询问同类操作)
```

2. 用户回复关键字：
   - **"同意"** / **"允许"** / **"yes"** / **"y"** / **"approve"** → 批准本次操作
   - **"同意全部"** / **"允许全部"** / **"yes all"** / **"approve all"** → 批准本次操作，且本会话中不再询问同类操作
   - **"拒绝"** / **"不同意"** / **"no"** / **"n"** / **"reject"** / **"deny"** → 拒绝操作
   - 不回复或超时 → 自动拒绝

3. Agent 根据审批结果继续或中止操作

#### 会话级审批缓存

当用户选择"同意全部"后，该会话中同类操作将不再触发审批：

- 文件操作按类型缓存（如 `file:write`）
- Shell 命令按通配符缓存（`shell:*`）
- 缓存仅在当前会话有效
- 使用 `/new` 或 `/clear` 清除会话时，审批缓存也会被清除

#### 安全建议

1. **最小权限原则**：仅将必要的用户加入 `AdminUsers`，其他用户加入 `WhitelistedUsers`
2. **谨慎审批**：仔细阅读审批请求中的操作详情，对可疑操作选择"拒绝"
3. **定期审查**：定期检查 `AdminUsers` 和 `WhitelistedUsers` 列表，移除离职或不再需要权限的用户
4. **工作区外操作**：默认配置下工作区外操作会被审批拦截，建议保持 `Tools.File.RequireApprovalOutsideWorkspace = true`

---

### 2.5 启动与验证

```bash
cd /your/workspace
dotcraft
```

启动成功后控制台输出：

```
[WeCom] Registered handler for: /dotcraft
Heartbeat started (interval: 1800s)
Cron service started (0 jobs)
WeCom Bot listening on http://0.0.0.0:9000
Registered bots:
  - http://0.0.0.0:9000/dotcraft
Press Ctrl+C to stop...
```

### 2.5 消息处理

DotCraft 支持三种消息处理器，按优先级路由：

#### 文本消息处理器（TextMessageHandler）

专门处理文本消息，优先级高于通用处理器。

```csharp
public delegate Task TextMessageHandler(
    string[] parameters,    // 消息内容分词后的参数数组
    WeComFrom from,         // 发送者信息（UserId, Name, Alias）
    IWeComPusher pusher     // 消息推送器
);
```

- 群聊消息中，如果第一个参数是 `@机器人名`，框架会自动跳过
- 消息内容会自动规范化（去除多余空白、换行符）

#### 通用消息处理器（CommonMessageHandler）

处理所有类型的消息（文本、图片、语音、文件、附件、图文混排）。

```csharp
public delegate Task CommonMessageHandler(
    WeComMessage message,   // 完整的企业微信消息对象
    IWeComPusher pusher     // 消息推送器
);
```

#### 事件消息处理器（EventMessageHandler）

处理机器人事件（加入/移出群聊、进入单聊）。

```csharp
public delegate Task<string?> EventMessageHandler(
    string eventType,       // 事件类型
    string chatType,        // 会话类型（single/group）
    WeComFrom from,         // 发送者信息
    IWeComPusher pusher     // 消息推送器
);
```

#### 消息与事件类型常量

```csharp
// 消息类型（WeComMsgType）
"event"       // 事件消息
"text"        // 文本消息
"image"       // 图片消息
"voice"       // 语音消息（仅单聊，智能机器人 API）
"file"        // 文件消息（仅单聊，智能机器人 API）
"attachment"  // 附件消息
"mixed"       // 图文混排消息

// 事件类型（WeComEventType）
"add_to_chat"        // 被添加到群聊
"delete_from_chat"   // 被移出群聊
"enter_chat"         // 进入单聊

// 会话类型（WeComChatType）
"single"     // 单聊
"group"      // 群聊
```

### 2.6 命令系统

企业微信机器人模式支持以下斜杠命令：

| 命令 | 说明 |
|------|------|
| `/new` 或 `/clear` | 清除当前会话，开始新对话 |
| `/help` | 显示可用命令列表 |
| `/heartbeat trigger` | 手动触发一次心跳检查 |
| `/cron list` | 查看所有定时任务 |
| `/cron remove <id>` | 删除指定定时任务 |

### 2.7 会话管理

每个用户的会话独立存储，命名规则为：

```
sessionId = "wecom_{chatId}_{userId}"
```

- 同一群聊中不同用户拥有独立会话
- 不同群聊中同一用户也拥有独立会话
- 所有会话共享长期记忆（`MEMORY.md`）

会话文件存储在 `.craft/sessions/` 目录下。

### 2.8 推送能力（IWeComPusher）

企业微信机器人模式下的消息推送器支持多种消息格式：

| 方法 | 说明 |
|------|------|
| `PushTextAsync` | 文本消息，支持 @提醒和可见性控制 |
| `PushMarkdownAsync` | Markdown 消息 |
| `PushImageAsync` | 图片消息（传入字节数组） |
| `PushNewsAsync` | 图文消息（最多 8 条） |
| `PushMiniProgramAsync` | 小程序卡片 |
| `PushVoiceAsync` | 语音消息（需先上传素材获取 media_id） |
| `PushFileAsync` | 文件消息（需先上传素材获取 media_id） |
| `UploadMediaAsync` | 上传临时素材，返回 media_id |
| `PushRawAsync` | 原始 JSON |

示例：

```csharp
await pusher.PushTextAsync("Hello World");

await pusher.PushTextAsync("重要通知",
    mentionedList: new List<string> { "@all" });

await pusher.PushTextAsync("机密信息",
    visibleToUser: new List<string> { "userid1" });

await pusher.PushMarkdownAsync(
    "# 标题\n- 列表项 1\n- 列表项 2");

await pusher.PushNewsAsync(new List<WeComArticle>
{
    new() {
        Title = "标题",
        Description = "描述",
        Url = "https://example.com",
        PicUrl = "https://example.com/pic.jpg"
    }
});

// 上传并发送语音
using var voiceStream = File.OpenRead("voice.amr");
var mediaId = await pusher.UploadMediaAsync(voiceStream, "voice.amr", "voice");
await pusher.PushVoiceAsync(mediaId);

// 上传并发送文件
using var fileStream = File.OpenRead("report.pdf");
var fileMediaId = await pusher.UploadMediaAsync(fileStream, "report.pdf", "file");
await pusher.PushFileAsync(fileMediaId);
```

> **注意**：企业微信推送（`WeCom` 配置节）仅支持纯文本（`text`）格式；而企业微信机器人模式（`WeComBot`）的 `IWeComPusher` 支持上述所有格式。
>
> **语音/文件限制**：上传临时素材的有效期为 3 天。语音支持 AMR 格式，文件大小上限参见企业微信官方文档。

### 2.9 语音与文件消息

企业微信智能机器人 API 支持在单聊中接收语音和文件消息。DotCraft 对这两种消息的处理方式如下：

#### 语音消息（接收）

企业微信会将语音消息自动转为文本（ASR），DotCraft 接收到后会将转写文本作为文本输入交给 AI Agent 处理。即用户发送语音等价于发送文本消息，Agent 会正常回复。

消息流转：

```
用户发送语音 → 企业微信 ASR 转文本 → DotCraft 接收 voice.content
  → 路由到 TextHandler → AI Agent 处理 → 回复文本
```

#### 文件消息（接收）

用户在单聊中发送文件时，DotCraft 会接收到文件的加密下载 URL，并在通用消息处理器中记录。当前默认行为是记录文件 URL 并回复确认信息。

#### 语音/文件消息（发送）

`IWeComPusher` 提供了 `PushVoiceAsync` 和 `PushFileAsync` 方法用于发送语音和文件消息。发送前需先通过 `UploadMediaAsync` 上传临时素材获取 `media_id`。

> **当前状态**：Agent 已内置 `WeComSendVoice` / `WeComSendFile` 工具，可在 WeCom Bot 模式下的对话中直接调用（详见 §2.10）。以下示例适用于需要在自定义处理器中直接操作底层 API 的场景。

二次开发示例：

```csharp
// 在自定义处理器中发送语音
using var voiceStream = File.OpenRead("voice.amr");
var mediaId = await pusher.UploadMediaAsync(voiceStream, "voice.amr", "voice");
await pusher.PushVoiceAsync(mediaId);

// 在自定义处理器中发送文件
using var fileStream = File.OpenRead("report.pdf");
var fileMediaId = await pusher.UploadMediaAsync(fileStream, "report.pdf", "file");
await pusher.PushFileAsync(fileMediaId);
```

#### 限制与注意事项

| 项目 | 说明 |
|------|------|
| 会话类型 | 语音和文件消息仅支持单聊，不支持群聊 |
| 回调格式 | 语音/文件通过智能机器人 API（JSON 格式）回调，非旧版 XML 推送 API |
| 素材有效期 | 上传的临时素材 3 天后过期 |
| 语音格式 | 仅支持 AMR 格式 |
| 语音转文本 | 由企业微信服务端完成，DotCraft 直接获取转写结果 |

> 参考文档：[企业微信智能机器人接口文档](https://developer.work.weixin.qq.com/document/path/100719)

### 2.10 Agent 内置工具（WeCom 模式）

在企业微信机器人模式下，Agent 可直接调用以下工具将内容发送到“当前会话”：

| 工具名 | 作用 | 备注 |
|-------|------|------|
| `WeComSendVoice(filePath)` | 发送语音（AMR 推荐） | 仅支持本地绝对路径；先上传素材再发送 |
| `WeComSendFile(filePath)` | 发送文件 | 仅支持本地绝对路径；先上传素材再发送 |

> 说明：以上工具基于当前消息上下文的 chatId 工作，无需额外填写用户 ID；若要点对点外发特定用户，可在自定义处理器中使用 `pusher.UploadMediaAsync` + `Push*Async` 组合实现更细粒度控制。

---

## 三、心跳与定时任务集成

### Heartbeat 通知

当同时启用 `Heartbeat.NotifyAdmin` 和 `WeCom.Enabled` 时，心跳执行结果会自动推送到企业微信群。

```json
{
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": true
    },
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
    }
}
```

- **QQ Bot 模式**：Heartbeat 结果同时发送到 QQ 管理员私聊和企业微信群
- **WeCom Bot 模式**：Heartbeat 结果仅发送到企业微信群（通过 `WeCom.WebhookUrl`）

### Cron 投递渠道

| `channel` | `to` | 投递目标 | 前置条件 |
|-----------|------|---------|----------|
| `"qq"` | `"group:<群号>"` | QQ 群 | QQ Bot 模式 |
| `"qq"` | `"<QQ号>"` | QQ 私聊 | QQ Bot 模式 |
| `"wecom"` | `"<ChatId>"` | 企业微信指定群 | WeCom Bot 模式 |
| `"wecom"` | 不填 | 企业微信（全局 Webhook） | 启用 `WeCom` 配置 |

> 从企业微信群对话中创建的 Cron 任务会自动关联当前群的 ChatId，投递时优先发送到该群；若 ChatId 尚未缓存（机器人重启后首次收消息前），则回退到 `WeCom.WebhookUrl`。

---

## 四、完整配置示例

### 仅启用企业微信推送

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
    }
}
```

### 启用企业微信机器人模式

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
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
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

### 同时启用 QQ Bot 与企业微信推送

```json
{
    "ApiKey": "sk-xxx",
    "Model": "gpt-4o-mini",
    "QQBot": {
        "Enabled": true,
        "Port": 6700,
        "AdminUsers": [123456789]
    },
    "WeCom": {
        "Enabled": true,
        "WebhookUrl": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY"
    },
    "Heartbeat": {
        "Enabled": true,
        "IntervalSeconds": 1800,
        "NotifyAdmin": true
    }
}
```

---

## 五、部署指南

请遵循企业微信官方文档部署：[企业微信 - 智能机器人](https://developer.work.weixin.qq.com/document/path/101039)

---

## 参考文档

- [企业微信群机器人配置说明](https://developer.work.weixin.qq.com/document/path/99110)
- [企业微信智能机器人接口文档](https://developer.work.weixin.qq.com/document/path/100719)
- [DotCraft 配置指南](./config_guide.md)
