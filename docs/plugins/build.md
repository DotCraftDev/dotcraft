# 创建插件

DotCraft 插件用于分发一组可复用能力。最快方式是让内置 `$plugin-creator` 先搭好插件结构，再根据你的场景补充说明、工具逻辑和验证步骤。

如果只是给自己当前项目补充一段工作流，优先创建普通 skill。需要把 skills、dynamic tools、图标和安装页信息一起打包分发时，再创建 plugin。

## 用 plugin creator 起步

在对话中直接描述你想要的插件：

```text
$plugin-creator 创建一个名为 External Process Echo 的插件，包含一个 skill 和一个外部进程 tool。
```

也可以把运行方式、语言和验证方式一起说清楚：

```text
$plugin-creator 创建一个本地插件，用 Python 进程提供 EchoText dynamic tool，并生成安装验证说明。
```

`plugin-creator` 会生成 DotCraft 插件目录、`.craft-plugin/plugin.json`、plugin-contained skill，以及可选的 process-backed dynamic tool scaffold。生成后通常只需要做三件事：

1. 替换 TODO 和示例文案。
2. 实现或调整 tool 进程逻辑。
3. 把插件安装到本地并在 Plugins 页面刷新验证。

## DotCraft 插件结构

DotCraft 使用 `.craft-plugin/plugin.json` 作为插件入口：

```text
my-plugin/
  .craft-plugin/
    plugin.json
  skills/
    my-skill/
      SKILL.md
  tools/
    tool_process.py
```

DotCraft v1 支持两类贡献：

| 内容 | 说明 |
|------|------|
| `skills` | 随插件分发的 DotCraft/Codex-compatible skills。 |
| `tools` | Agent 可调用的 DotCraft dynamic tools，可由本地 stdio 进程执行。 |

## 了解最小 manifest

一般不需要手写完整 manifest。只在排查或高级定制时了解基本结构即可：

```json
{
  "schemaVersion": 1,
  "id": "my-plugin",
  "version": "0.1.0",
  "displayName": "My Plugin",
  "description": "Package reusable DotCraft capabilities.",
  "skills": "./skills/",
  "interface": {
    "displayName": "My Plugin",
    "shortDescription": "Reusable workflow for DotCraft",
    "developerName": "Your team",
    "category": "Coding"
  }
}
```

带外部进程 dynamic tool 的插件还会包含 `tools` 和 `processes`。建议让 `plugin-creator` 生成这部分结构，再按业务修改。

## 本地验证

1. 把插件放到 `.craft/plugins/<plugin-id>/`，或把插件目录加入 `Plugins.PluginRoots`。
2. 打开 Desktop 的 Plugins 页面并点击 **Refresh / 刷新**。
3. 打开插件详情，确认 tools 和 plugin-contained skills 显示正确。
4. 在对话中描述要调用的能力，或点击 **Try in chat / 在对话中试用**。
5. 如果插件没有出现，查看 Plugins 页面 diagnostics。

## 什么时候看参考

需要处理这些高级内容时，再查看完整参考：

- process-backed dynamic tool 的 JSON-RPC 协议
- tool 的 `approval` 元数据
- manifest 路径规则
- 完整字段和 schema

参考位置：

- `src/DotCraft.Core/Skills/BuiltIn/plugin-creator/references/plugin-json-spec.md`
- [Plugin Architecture Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/plugin-architecture.md)

## 相关文档

- [安装和使用插件](./install.md)
- [Skills 搜索与安装](../skills/marketplace.md)
- [AppServer Protocol](../reference/appserver-protocol.md)
