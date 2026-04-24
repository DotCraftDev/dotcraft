# DotCraft Skills Samples

`samples/skills/` 提供了可复制到工作区的 Skill 示例，用来约束 agent 在特定项目里的开发流程、模块规范和大型功能交付步骤。

## 示例内容

| 目录 | 用途 |
|------|------|
| [dev-guide](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/dev-guide) | 项目开发规范示例，包含模块开发参考文档。 |
| [feature-workflow](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/feature-workflow) | 大型功能开发工作流示例，用于拆解、实现和验证复杂需求。 |

## 使用方式

1. 在你的工作区创建 `.craft/skills/`。
2. 将需要的示例目录复制进去，例如 `.craft/skills/dev-guide/`。
3. 根据项目实际约定修改 `SKILL.md` 和 `references/` 中的说明。
4. 在任务中明确要求 agent 使用对应 skill，或在项目说明中约定默认使用。

## 建议

- `dev-guide` 适合沉淀长期稳定的工程规范。
- `feature-workflow` 适合复杂功能或跨模块改动。
- 复制后优先保留结构，逐步替换为你项目自己的术语、路径和验收标准。
