# DotCraft Samples

这些示例帮助你从不同入口快速验证 DotCraft：Workspace 模板、API 调用、AG-UI 前端、自动化任务、Hooks 和内置 Skills。

## Quick Start

第一次验证建议按这个顺序：

1. 先跑 [Workspace Sample](./workspace.md)，确认工作区结构和模型配置。
2. 再按目标选择 API、AG-UI、Automations 或 Hooks 示例。
3. 最后回到真实项目，把示例配置迁移到项目的 `.craft/`。

## Configuration

示例源码保留在 [samples/](https://github.com/DotHarness/dotcraft/tree/master/samples)。文档页说明使用方式，具体代码、模板和脚本请从源码目录查看。

## Usage Examples

| 目标 | 示例 |
|------|------|
| 体验完整工作区 | [Workspace Sample](./workspace.md) |
| 调用 OpenAI 兼容 API | [API Samples](./api.md) |
| 运行 AG-UI 前端 | [AG-UI Client](./ag-ui-client.md) |
| 配置本地 / GitHub 自动化 | [Automations Samples](./automations.md) |
| 编写生命周期 Hooks | [Hooks Samples](./hooks.md) |
| 准备项目启动上下文 | [Bootstrap Samples](./bootstrap.md) |
| 复用内置 Skill 模板 | [Skills Samples](./skills.md) |

## Advanced Topics

- 示例中的配置片段可以迁移到真实项目的 `.craft/config.json`。
- 涉及 token 的示例优先使用环境变量，不要把密钥写入仓库。
- 自动化和 GitHub 示例建议先在测试仓库验证。

## Troubleshooting

### 示例找不到源码文件

确认你在 DotCraft 仓库根目录，示例源码位于 `samples/`，文档站页面位于 `docs/samples/`。

### 示例配置复制后不生效

确认配置放在当前工作区 `.craft/config.json`，并重启相关 Host。启动级字段不会自动热更新。

### 不知道先跑哪个示例

先跑 Workspace Sample；它覆盖工作区结构、配置位置和推荐启动顺序。
