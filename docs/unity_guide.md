# Unity 集成指南

DotCraft 通过 Agent Client Protocol (ACP) 与 Unity 编辑器无缝集成。本指南介绍 Unity 集成的安装、配置和使用方法。

## 架构

Unity 集成包含两个组件：

1. **服务端模块** (`DotCraft.Unity`)：提供 4 个只读工具，用于理解 Unity 项目状态
2. **Unity 客户端包** (`com.dotcraft.unityclient`)：Unity 编辑器扩展，提供编辑器内聊天界面

```
┌─────────────────────┐         ACP 协议          ┌─────────────────────┐
│   DotCraft 服务端   │◄──────────────────────────►│   Unity 编辑器      │
│                     │      _unity/* 扩展方法      │                     │
│  - UnityModule      │                               │  - 编辑器窗口      │
│  - 工具提供者       │                               │  - 协议客户端      │
└─────────────────────┘                               └─────────────────────┘
```

## 前置要求

- Unity 2022.3 或更高版本
- DotCraft 已安装并可通过命令行访问
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) 包管理器
- `System.Text.Json 9.0.10`（通过 NuGetForUnity 安装）

## 安装步骤

### 步骤 1：安装 NuGetForUnity

如果尚未安装 NuGetForUnity：

1. 在 Unity 中打开 **Window → Package Manager**
2. 点击 **+ → Add package from git URL**
3. 输入：`https://github.com/GlitchEnzo/NuGetForUnity.git`

### 步骤 2：安装 System.Text.Json

1. 在 Unity 中打开 **NuGet → Manage NuGet Packages**
2. 搜索 `System.Text.Json`
3. 选择版本 `9.0.10` 并点击 **Install**

### 步骤 3：安装 DotCraft Unity 客户端包

**方式 A — Git URL**（推荐）：

1. 在 Unity 中打开 **Window → Package Manager**
2. 点击 **+ → Add package from git URL**
3. 输入：
   ```
   https://github.com/DotHarness/DotCraft.git?path=src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient
   ```

**方式 B — 本地路径**：

1. 克隆 DotCraft 仓库
2. 在 Unity 中打开 **Window → Package Manager**
3. 点击 **+ → Add package from disk**
4. 导航到 `src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/package.json` 并选择

### 步骤 4：配置 DotCraft

确保 DotCraft 已安装并在 `~/.craft/config.json` 中配置了 LLM API 密钥：

```json
{
    "ApiKey": "sk-your-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1"
}
```

## 快速开始

1. 在 Unity 中通过 **Tools → DotCraft Assistant** 打开 DotCraft 窗口
2. 点击 **Connect** 启动 DotCraft 进程并建立 ACP 会话
3. 在输入框中输入消息并按 **Enter**（或点击 **Send**）
4. AI 助手现在可以与您的 Unity 项目交互

## 编辑器窗口

### 状态栏

状态栏显示当前连接状态：

| 状态 | 指示颜色 |
|------|---------|
| 已断开 | 红色 |
| 连接中 | 黄色 |
| 已连接 | 绿色 |

### 模式和模型选择器

连接后，下拉菜单允许切换：
- **Mode**：不同的智能体模式（如 agent、ask）
- **Model**：当前会话使用的 LLM 模型

### 会话管理

- 通过 **Session** 下拉菜单在现有会话间切换
- 通过选择 **+ New Session** 或点击 **+** 按钮开始新会话

### 聊天面板

- 消息按时间顺序显示
- 智能体响应支持 Markdown 渲染
- 工具调用及其结果显示为可折叠的内联条目

### 附加资源

从 **Project** 窗口拖动任何 Unity 资源到 DotCraft 窗口，将其附加到消息中。附加的资源显示为输入框上方的标签。

## 配置

打开 **Edit → Project Settings → DotCraft** 进行配置：

| 设置 | 默认值 | 描述 |
|------|--------|------|
| **Command** | `dotcraft` | 可执行文件名称或 DotCraft 的完整路径 |
| **Arguments** | `-acp` | DotCraft 的命令行参数 |
| **Workspace Path** | *(空)* | 工作目录（默认为 Unity 项目根目录） |
| **Environment Variables** | *(空)* | DotCraft 进程的键值对 |
| **Auto Reconnect** | `true` | Domain Reload 后自动重新连接 |
| **Verbose Logging** | `false` | 将 DotCraft stderr 输出到 Unity Console |
| **Request Timeout (s)** | `30` | ACP 请求的最大等待时间（5–120 秒） |
| **Max History Messages** | `1000` | 聊天视图中的最大消息数 |
| **Enable Builtin Unity Tools** | `true` | 启用内置 `_unity/*` 扩展方法。使用外部 Unity 集成时可关闭 |

### 通过环境变量配置 API 密钥

为避免将密钥存储在版本控制中：

1. 打开 **Edit → Project Settings → DotCraft**
2. 在 **Environment Variables** 下，点击 **+ Add Variable**
3. 将键设置为 `DOTCRAFT_API_KEY`，值粘贴您的 API 密钥

## 内置工具

连接后，DotCraft 提供 4 个 Unity 只读工具，帮助 AI 助手理解项目状态：

### 场景工具

| 工具 | 描述 |
|------|------|
| `unity_scene_query` | 查询场景层级结构，可选择包含组件详情 |
| `unity_get_selection` | 获取 Unity 编辑器中当前选中的对象 |

### 控制台工具

| 工具 | 描述 |
|------|------|
| `unity_get_console_logs` | 检索最近的 Unity Console 日志条目 |

### 项目工具

| 工具 | 描述 |
|------|------|
| `unity_get_project_info` | 获取 Unity 版本、项目名称和包信息 |

这些只读工具开箱即用，无需额外配置，让 AI 助手能够：
- 理解场景结构和对象关系
- 了解用户当前关注的对象
- 查看编译错误和警告
- 获取项目上下文信息

## 扩展功能：SkillsForUnity

如需完整的 Unity 操作能力（创建、修改、删除 GameObject，执行菜单等），推荐安装 [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) 插件。

### SkillsForUnity 功能

SkillsForUnity 提供 100+ Unity 编辑器技能，包括：

- **GameObject 管理**：创建、删除、批量操作
- **组件操作**：添加、修改属性、批量设置
- **场景管理**：创建、加载、保存、截图
- **资源操作**：查找、导入、管理
- **UI 构建**：创建 Canvas、按钮、布局
- **材质/Prefab 管理**
- **编辑器控制**：播放、停止、撤销、菜单执行
- **高级模块**：Cinemachine、Terrain、Animator、NavMesh、Timeline

### 安装 SkillsForUnity

1. 在 Unity 中打开 **Window → Package Manager**
2. 点击 **+ → Add package from git URL**
3. 输入：`https://github.com/BestyAIGC/Unity-Skills.git?path=SkillsForUnity`
4. 安装完成后，通过 **Window → UnitySkills → Start Server** 启动 HTTP 服务器
5. 通过 **Window → UnitySkills → Install to Claude Code** 安装技能描述

### 架构对比

| 特性 | DotCraft 内置工具 | SkillsForUnity | unity-mcp |
|------|------------------|----------------|-----------|
| **安装复杂度** | 开箱即用 | 需要启动 HTTP 服务器 | 需要 Python + HTTP 服务器 |
| **功能范围** | 4 个只读工具 | 100+ 技能 | 30+ 工具 |
| **通信方式** | ACP 协议（stdio） | HTTP REST API | MCP 协议（HTTP/stdio） |
| **跨 IDE 支持** | 仅 DotCraft | 多种 IDE | 多种 IDE |
| **适用场景** | 理解项目状态 | 完整 Unity 操作 | 跨平台 Unity 操作 |

## 扩展功能：unity-mcp

[unity-mcp](https://github.com/CoplayDev/unity-mcp) 是另一个 Unity 集成方案，采用 MCP (Model Context Protocol) 协议，支持多种 AI IDE。

### unity-mcp 功能

unity-mcp 提供 30+ Unity 操作工具，包括：

- **场景管理**：加载、保存、创建、查询层级
- **GameObject 管理**：创建、修改、变换、删除
- **组件操作**：添加、移除、配置
- **资源管理**：创建、修改、搜索
- **材质/Prefab/脚本管理**
- **批量执行**：批量操作效率提升 10-100 倍
- **控制台读取**：获取 Unity Console 输出
- **测试运行**：运行 Unity 测试

### 安装 unity-mcp

**前置要求**：
- Unity 2021.3 LTS 或更高版本
- Python 3.10+ 和 [uv](https://docs.astral.sh/uv/) 包管理器

**安装步骤**：

1. 在 Unity 中打开 **Window → Package Manager**
2. 点击 **+ → Add package from git URL**
3. 输入：`https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`
4. 打开 **Window → MCP for Unity**，点击 **Start Server**
5. 从下拉菜单选择 MCP 客户端并点击 **Configure**

![unity-mcp](https://github.com/DotHarness/resources/raw/master/dotcraft/unity-mcp.png)

### 推荐使用方式

1. **基础使用**：DotCraft 内置的只读工具满足日常项目理解需求
2. **高级操作**：安装 SkillsForUnity 或 unity-mcp 获得完整的 Unity 编辑器控制能力
3. **关闭内置工具**：如使用外部 Unity 集成，可在 **Project Settings → DotCraft** 中关闭 **Enable Builtin Unity Tools**
4. **选择建议**：
   - **SkillsForUnity**：功能最丰富（100+ 技能），适合深度 Unity 开发
   - **unity-mcp**：MCP 协议兼容，适合跨 AI IDE 使用
   - **内置工具**：最简单，适合快速上手

## 权限审批

当 DotCraft 请求执行高风险操作时，将显示审批面板，提供三个选项：

| 按钮 | 操作 |
|------|------|
| **Allow** | 批准本次请求 |
| **Allow Always** | 批准本次会话中所有类似请求 |
| **Reject** | 拒绝本次请求 |

## Domain Reload 处理

当 Unity 触发 Domain Reload（例如脚本编译后），客户端将：

1. 保存当前会话 ID
2. 干净地终止 DotCraft 进程
3. 重启并重新连接到同一会话（需要启用 **Auto Reconnect**）

## 工作区

默认情况下，Unity 项目根目录（包含 `Assets/` 的文件夹）作为 DotCraft 工作区。工作区必须包含 `.craft/` 文件夹及有效配置。

如需覆盖工作区路径，可在 **Edit → Project Settings → DotCraft** 中设置。

## 使用示例

### 查询场景层级

```
用户: 显示场景中的所有 GameObject
AI: [使用 unity_scene_query 工具]
    找到 15 个 GameObject：
    - Main Camera
    - Directional Light
    - Canvas
    ...
```

### 获取控制台日志

```
用户: 检查是否有编译错误
AI: [使用 unity_get_console_logs 工具，过滤条件 "error"]
    找到 2 个错误：
    - Assets/Scripts/Player.cs(45): error CS0103: 'velocity' does not exist
    - Assets/Scripts/Enemy.cs(12): error CS0246: Type 'Navigation' not found
```

### 获取项目信息

```
用户: 这个项目使用什么 Unity 版本？
AI: [使用 unity_get_project_info 工具]
    项目信息：
    - Unity 版本：2022.3.15f1
    - 项目名称：MyGame
    - 已安装包：12 个
```

## 故障排除

| 症状 | 可能原因 | 解决方法 |
|------|---------|---------|
| "Failed to start DotCraft process" | `dotcraft` 不在 PATH 中 | 安装 DotCraft 并添加到 PATH，或在 **Command** 中设置完整路径 |
| 卡在 "Connecting…" | DotCraft 启动时崩溃 | 启用 **Verbose Logging** 并检查 Unity Console 中的 stderr 输出 |
| 每次编译后窗口断开连接 | **Auto Reconnect** 已禁用 | 在 Project Settings 中启用 **Auto Reconnect** |
| 权限面板从不消失 | Domain Reload 期间之前的回调丢失 | 断开并重新连接以开始新会话 |
| 工具不可用 | ACP 客户端未声明 `_unity` 扩展 | 确保 DotCraft 服务端模块已加载（ACP 模式已启用） |

## 提示

- 在初始设置期间使用 **Verbose Logging** 诊断连接问题
- 为 API 密钥配置环境变量，而不是修改全局配置
- 可将工作区路径设置为父目录，以便在多个 Unity 项目间共享记忆
- 使用 SkillsForUnity 或 unity-mcp 获得完整的 Unity 编辑器操作能力
- 如使用外部 Unity 集成，可在设置中关闭内置 Unity 工具

## 相关链接

- [配置指南](./config_guide.md) - DotCraft 配置选项
- [ACP 模式指南](./acp_guide.md) - Agent Client Protocol 详情
- [Unity 客户端 README](https://github.com/DotHarness/DotCraft/tree/master/src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient) - 包文档
- [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) - HTTP REST API 方式的 Unity 技能库
- [unity-mcp](https://github.com/CoplayDev/unity-mcp) - MCP 协议方式的 Unity 集成工具
