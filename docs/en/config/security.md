# DotCraft Security Configuration

DotCraft security settings control file access, shell execution, web fetching, and sandbox isolation. Put project-specific security policy in workspace config so each project can use the right boundary.

## Quick Start

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

This blocks sensitive directories and requires approval for file or shell paths outside the workspace.

## Configuration

### File Access Blacklist

`Security.BlacklistedPaths` defines paths that DotCraft must not access. The blacklist applies across CLI, Desktop, external channels, and automation entry points.

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

Blacklist behavior:

- `ReadFile`, `WriteFile`, `EditFile`, `GrepFiles`, and `FindFiles` are rejected for blacklisted paths.
- Shell commands that reference blacklisted paths are rejected.
- Blacklist checks run before workspace boundary checks.
- Absolute paths and `~` expansion are supported, including child paths.

### Workspace Boundary

DotCraft analyzes paths before running shell commands. It covers Unix absolute paths, home-directory paths, environment-variable paths, Windows drive paths, UNC paths, and common safe device paths.

When a referenced path resolves outside the workspace:

- `Tools.Shell.RequireApprovalOutsideWorkspace = false` rejects the command.
- `Tools.Shell.RequireApprovalOutsideWorkspace = true` asks the current interaction source for approval.

File tools also expand `~`, `$HOME`, `${HOME}`, and `%ENV%` so workspace boundary checks stay consistent.

### Tool Security Fields

| Field | Description | Default |
|-------|-------------|---------|
| `Tools.File.RequireApprovalOutsideWorkspace` | Whether file operations outside the workspace require approval | `true` |
| `Tools.File.MaxFileSize` | Maximum readable file size in bytes | `10485760` |
| `Tools.Shell.RequireApprovalOutsideWorkspace` | Whether shell commands outside the workspace require approval | `true` |
| `Tools.Shell.Timeout` | Shell command timeout in seconds | `300` |
| `Tools.Shell.MaxOutputLength` | Maximum shell output length in characters | `10000` |
| `Tools.Web.MaxChars` | Maximum web fetch characters | `50000` |
| `Tools.Web.Timeout` | Web request timeout in seconds | `300` |
| `Tools.Web.SearchMaxResults` | Default web search result count | `5` |
| `Tools.Web.SearchProvider` | Search provider: `Bing` / `Exa` | `Exa` |
| `Tools.Lsp.Enabled` | Enables the built-in LSP tool | `false` |
| `Tools.Lsp.MaxFileSize` | Maximum file size for LSP open/sync | `10485760` |

### Sandbox Mode

[OpenSandbox](https://github.com/alibaba/OpenSandbox) can run Shell and File tools inside an isolated Docker container.

Prerequisites:

```bash
pip install opensandbox-server
opensandbox-server
```

Example:

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

| Field | Description | Default |
|-------|-------------|---------|
| `Tools.Sandbox.Enabled` | Enables sandbox mode | `false` |
| `Tools.Sandbox.Domain` | OpenSandbox service address | `localhost:5880` |
| `Tools.Sandbox.ApiKey` | OpenSandbox API key | Empty |
| `Tools.Sandbox.UseHttps` | Uses HTTPS | `false` |
| `Tools.Sandbox.Image` | Sandbox container image | `ubuntu:latest` |
| `Tools.Sandbox.TimeoutSeconds` | Sandbox timeout in seconds | `600` |
| `Tools.Sandbox.Cpu` | Container CPU limit | `1` |
| `Tools.Sandbox.Memory` | Container memory limit | `512Mi` |
| `Tools.Sandbox.NetworkPolicy` | Network policy: `deny` / `allow` / `custom` | `allow` |
| `Tools.Sandbox.AllowedEgressDomains` | Custom allowed egress domains | `[]` |
| `Tools.Sandbox.IdleTimeoutSeconds` | Idle timeout in seconds | `300` |
| `Tools.Sandbox.SyncWorkspace` | Syncs the workspace into the container | `true` |

## Usage Examples

| Scenario | Recommendation |
|----------|----------------|
| Personal local project | Keep outside-workspace approval and blacklist SSH, cloud credentials, and password-manager directories |
| Shared team workspace | Store security policy in project `.craft/config.json` so every entry point enforces it |
| External channel or bot | Require outside-workspace approval and limit tools and network access |
| Automation tasks | Enable sandboxing or narrow `EnabledTools` according to task risk |

## Troubleshooting

### A command inside the workspace is still rejected

Check whether the command string references an outside path such as `~/.ssh`, `/etc`, `C:\Users`, or an environment variable that expands outside the workspace.

### Sandbox does not start

Confirm Docker and `opensandbox-server` are running, then check `Tools.Sandbox.Domain`, `ApiKey`, and network policy.

### Web search or fetch fails

Check `Tools.Web.SearchProvider`, `Tools.Web.Timeout`, `Tools.Web.MaxChars`, and the network environment.
