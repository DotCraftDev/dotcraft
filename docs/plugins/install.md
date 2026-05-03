# 安装和使用插件

DotCraft Plugin 用来把可复用的工作区能力打包成可安装扩展。一个插件可以提供：

| 内容 | 说明 |
|------|------|
| Dynamic tool | Agent 可以调用的工具。插件可以通过本地 stdio 进程执行这些工具。 |
| Skill | 随插件分发的工作流说明，安装并启用插件后会进入 DotCraft 的 skill 列表。 |
| 元数据 | 名称、描述、开发者、分类、图标、默认 prompt 和相关链接。 |

插件内置的 skill 跟随插件生命周期。启用插件时可用，禁用或移除插件后不再进入 Agent 上下文。

## 在 Desktop 中安装

1. 打开 DotCraft Desktop。
2. 进入 **Plugins / 插件** 页面。
3. 搜索或浏览插件，打开插件详情。
4. 点击 **Install / 安装**。
5. 安装完成后，可以点击 **Try in chat / 在对话中试用**，或在新对话中直接描述你想完成的任务。

如果你不指定插件，DotCraft 也可以根据任务自动选择已安装且启用的插件能力。

## 启用、禁用和移除

DotCraft 把插件状态分成三类：

| 操作 | 含义 |
|------|------|
| 安装 | 将插件加入当前工作区可用能力。 |
| 启用 / 禁用 | 保留插件文件，只控制它是否进入 Agent 上下文。 |
| 移除 | 从当前工作区移除插件。对 `.craft/plugins/<plugin-id>` 下的本地插件，这会删除对应插件目录。 |

插件管理页可以批量启用或禁用已安装插件。禁用插件后，它贡献的 tools 和 plugin-contained skills 都不会进入 Agent 上下文。

## 安装本地插件

开发或测试本地插件时，可以使用两种方式：

```text
.craft/plugins/<plugin-id>/.craft-plugin/plugin.json
```

把 plugin root 复制到当前工作区的 `.craft/plugins/<plugin-id>/` 后，打开 Plugins 页面并点击 **Refresh / 刷新**。这种安装方式可以在 Desktop 插件详情页移除，移除时会删除该插件目录。

也可以把 plugin root 或 plugin container 加入 `Plugins.PluginRoots`：

```json
{
  "Plugins": {
    "PluginRoots": ["./samples/plugins"]
  }
}
```

`Plugins.PluginRoots` 适合让 DotCraft 直接读取你正在维护的外部插件目录。Desktop 不会删除这些外部目录；需要移除时，请从配置中移除对应 root 或在文件系统中自行管理。

## 验证插件

安装或修改插件后：

1. 在 Plugins 页面点击 **Refresh / 刷新**。
2. 搜索插件名称或 ID。
3. 打开详情页确认 tools、skills 和链接信息。
4. 如果插件没有出现，查看页面上的 diagnostics 提示，通常会包含 manifest 路径和错误原因。

## 安全与信任

安装插件会把新的 tools 和 skills 加入工作区能力范围。启用带 `process` backend 的插件后，DotCraft 可以启动插件 manifest 中声明的本地 stdio 进程来执行 dynamic tools。只安装和启用你信任来源、代码和依赖的插件。

插件 tool 调用仍会经过 DotCraft 的会话、审批和工具调用记录。插件详情中的网站、隐私政策和服务条款链接用于帮助你确认插件来源和行为边界。

## 相关文档

- [创建插件](./build.md)
- [Skills 搜索与安装](../skills/marketplace.md)
- [AppServer Protocol](../reference/appserver-protocol.md)
- [Plugin Architecture Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/plugin-architecture.md)
