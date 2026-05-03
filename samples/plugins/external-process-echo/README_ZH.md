# External Process Echo 插件

这个示例插件提供：

- 一个随插件分发的 skill：`external-process-echo`
- 一个进程驱动的 dynamic tool：`EchoText`
- 一个 Python JSON-RPC stdio 进程：`tools/demo_tool.py`

## 安装

把 sample plugin root 复制到当前工作区：

```powershell
New-Item -ItemType Directory -Force .craft/plugins
Copy-Item -Recurse samples/plugins/external-process-echo .craft/plugins/external-process-echo
```

manifest 必须位于：

```text
.craft/plugins/external-process-echo/.craft-plugin/plugin.json
```

也可以保留 sample 原位置，把它的父目录加入 `Plugins.PluginRoots`：

```json
{
  "Plugins": {
    "PluginRoots": ["./samples/plugins"]
  }
}
```

复制安装和 `Plugins.PluginRoots` 二选一即可，不需要同时配置。复制或修改 plugin roots 后，在插件页点击 **Refresh / 刷新**。

复制到 `.craft/plugins/<plugin-id>` 的插件可以在 Desktop 插件详情页移除；通过 `Plugins.PluginRoots` 加载的插件由外部目录管理，Desktop 不会删除这些目录。

## 在插件页验证

打开 Plugins 页面并点击 **Refresh / 刷新**。插件应出现在 **Installed locally / 本地已安装** 区域。如果没有出现，搜索 `external-process-echo`，并查看页面上的 diagnostics 提示。

## 试用

在当前工作区开启对话并输入：

```text
Use External Process Echo to echo "hello from plugin tools".
```

DotCraft 会启动 manifest 中声明的 Python 进程，先发送 `plugin/initialize`，再通过 `plugin/toolCall` 调用 `EchoText`。

## 说明

- 需要能在 `PATH` 中通过 `python` 启动 Python。
- MCP server 继续通过 `McpServers` 配置，不写入 plugin manifest。
- 进程每次请求都会收到 `workspaceRoot` 和 `pluginRoot`。
