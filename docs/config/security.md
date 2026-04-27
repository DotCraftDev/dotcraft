# DotCraft 安全配置

DotCraft 的安全配置控制文件访问、Shell 执行、Web 抓取和沙箱隔离。建议把安全策略放在工作区配置中，让每个项目按自己的风险边界运行。

## 快速开始

```json
{
  "Security": {
    "BlacklistedPaths": [
      "~/.ssh",
      "~/.gnupg",
      "~/.aws"
    ]
  },
  "Tools": {
    "File": {
      "RequireApprovalOutsideWorkspace": true
    },
    "Shell": {
      "RequireApprovalOutsideWorkspace": true,
      "Timeout": 300
    }
  }
}
```

这份配置会阻止敏感目录访问，并要求工作区外文件或 Shell 路径经过审批。

## 配置

### 文件访问黑名单

`Security.BlacklistedPaths` 配置禁止访问的路径列表。黑名单全局生效，CLI、Desktop、外部渠道和自动化入口都会拦截。

```json
{
  "Security": {
    "BlacklistedPaths": [
      "~/.ssh",
      "/etc/shadow",
      "C:\\Windows\\System32"
    ]
  }
}
```

黑名单行为：

- `ReadFile`、`WriteFile`、`EditFile`、`GrepFiles`、`FindFiles` 对黑名单路径的操作会被拒绝。
- Shell 命令引用黑名单路径时会被拒绝。
- 黑名单优先于工作区边界检查。
- 支持绝对路径和 `~` 展开，并检查子路径。

### 工作区边界

DotCraft 会在执行 Shell 命令前分析路径，覆盖 Unix 绝对路径、家目录路径、环境变量路径、Windows 盘符路径、UNC 路径和常见安全设备路径。

若命令中引用的任意路径解析后位于工作区之外：

- `Tools.Shell.RequireApprovalOutsideWorkspace = false` 时直接拒绝。
- `Tools.Shell.RequireApprovalOutsideWorkspace = true` 时向当前交互源发起审批。

文件工具也会展开 `~`、`$HOME`、`${HOME}` 和 `%ENV%`，确保工作区边界检查一致。

### 工具安全字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.File.RequireApprovalOutsideWorkspace` | 工作区外文件操作是否需要审批 | `true` |
| `Tools.File.MaxFileSize` | 最大可读取文件大小（字节） | `10485760` |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | 工作区外 Shell 命令是否需要审批 | `true` |
| `Tools.Shell.Timeout` | Shell 命令超时时间（秒） | `300` |
| `Tools.Shell.MaxOutputLength` | Shell 命令最大输出长度（字符） | `10000` |
| `Tools.Web.MaxChars` | Web 抓取最大字符数 | `50000` |
| `Tools.Web.Timeout` | Web 请求超时时间（秒） | `300` |
| `Tools.Web.SearchMaxResults` | 联网搜索默认返回结果数 | `5` |
| `Tools.Web.SearchProvider` | 搜索引擎提供商：`Bing` / `Exa` | `Exa` |
| `Tools.Lsp.Enabled` | 是否启用内置 LSP 工具 | `false` |
| `Tools.Lsp.MaxFileSize` | LSP 打开或同步文件时允许的最大文件大小 | `10485760` |

### 沙箱模式

通过 [OpenSandbox](https://github.com/alibaba/OpenSandbox) 可以把 Shell 和 File 工具执行环境放到隔离的 Docker 容器中。

前置条件：

```bash
pip install opensandbox-server
opensandbox-server
```

配置示例：

```json
{
  "Tools": {
    "Sandbox": {
      "Enabled": true,
      "Domain": "localhost:5880",
      "Image": "ubuntu:latest",
      "NetworkPolicy": "allow",
      "SyncWorkspace": true
    }
  }
}
```

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Tools.Sandbox.Enabled` | 是否启用沙箱模式 | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox 服务地址 | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API Key | 空 |
| `Tools.Sandbox.UseHttps` | 是否使用 HTTPS | `false` |
| `Tools.Sandbox.Image` | 沙箱容器 Docker 镜像 | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | 沙箱超时时间（秒） | `600` |
| `Tools.Sandbox.Cpu` | 容器 CPU 限制 | `1` |
| `Tools.Sandbox.Memory` | 容器内存限制 | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | 网络策略：`deny` / `allow` / `custom` | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | 自定义允许出站域名列表 | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | 空闲超时（秒） | `300` |
| `Tools.Sandbox.SyncWorkspace` | 是否同步 workspace 到容器 | `true` |

## 使用示例

| 场景 | 建议 |
|------|------|
| 个人本地项目 | 保留工作区外审批，黑名单加入 SSH、云厂商凭据和密码管理目录 |
| 团队共享工作区 | 把安全策略写入项目 `.craft/config.json`，让所有入口一致执行 |
| 外部渠道或机器人 | 启用工作区外审批，并限制工具和网络访问 |
| 自动化任务 | 根据任务需要开启沙箱或收紧 `EnabledTools` |

## 故障排查

### 命令明明在工作区内仍被拒绝

检查命令字符串是否引用了工作区外路径，例如 `~/.ssh`、`/etc`、`C:\Users` 或环境变量展开后的路径。

### 沙箱无法启动

确认 Docker 和 `opensandbox-server` 正在运行，并检查 `Tools.Sandbox.Domain`、`ApiKey` 和网络策略。

### Web 搜索或抓取失败

检查 `Tools.Web.SearchProvider`、`Tools.Web.Timeout`、`Tools.Web.MaxChars` 和网络环境。
