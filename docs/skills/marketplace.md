# Skills 搜索与安装

DotCraft Desktop 的 Skills 页面可以同时搜索本地已安装技能和外部技能市场。你可以先在当前工作区查看已有技能，再从 SkillHub / ClawHub 安装适合项目的第三方技能。

![Skills 页面](https://github.com/DotHarness/resources/raw/master/dotcraft/skills.png)

## 技能来源

DotCraft 会在 Skills 页面展示三类本地技能：

| 来源 | 说明 |
|------|------|
| 系统 | DotCraft 内置技能，随应用提供。 |
| 工作区 | 当前工作区 `.craft/skills/` 下的技能。 |
| 个人 | 用户全局技能，可跨工作区使用。 |

技能市场搜索会同时查询：

- SkillHub
- ClawHub

市场结果不是默认启用的本地技能。只有安装后，它们才会写入当前工作区并出现在本地技能列表中。

## 在 Desktop 中搜索

1. 打开 DotCraft Desktop。
2. 进入 **Skills / 技能** 页面。
3. 在浏览页搜索框输入关键词。

搜索框会做两件事：

- 过滤当前已安装的本地技能。
- 当有查询词时，搜索 SkillHub / ClawHub 的市场结果。

![Skill market 搜索结果](https://github.com/DotHarness/resources/raw/master/dotcraft/skill-hub.png)

来源筛选可切换 `全部 / 系统 / 个人 / 市场`。筛选只影响当前浏览结果，不会改变技能是否启用。

## 从市场安装

1. 在搜索结果中点击一个市场技能。
2. 在详情页阅读 README、描述和来源链接。
3. 点击 **Install with DotCraft**，或在需要时点击更新、重新安装。
4. DotCraft 会启动一个 Agent 安装流程，检查当前工作区、系统环境和可用工具，并在发现具体环境差异时优化技能。
5. 安装完成后刷新本地技能列表。

<video controls src="https://github.com/DotHarness/resources/raw/master/dotcraft/skill_variant.mp4" style="width: 100%; border-radius: 8px;"></video>

<p class="caption">Desktop 中通过 DotCraft 安装市场技能，并生成适合本地环境的变体。</p>

市场技能会安装到当前工作区：

```text
.craft/skills/<skill-name>/
```

DotCraft 会在技能目录内写入安装记录：

```text
.craft/skills/<skill-name>/.dotcraft-market.json
```

这份记录用于识别来源、版本和更新状态。若工作区已经存在同名技能，Desktop 会要求确认后再覆盖或更新。

## Skill Variant 变体

通过 **Install with DotCraft** 安装市场技能时，Agent 会先保留原始技能，再根据当前工作区和运行环境生成优化版本。

优化版本会保存为 Variant（变体），不会直接覆盖原始技能。后续 Agent 使用技能时，DotCraft 会优先使用当前有效的变体；如果你想回到市场安装时的原始内容，可以随时在 Skills 页面中恢复原版本。

## 管理启用状态

浏览页只负责发现、查看和安装技能。要批量启用或禁用技能：

1. 点击 Skills 页右上角的 **管理**。
2. 在管理页搜索已安装技能。
3. 使用每一行右侧的开关启用或禁用技能。

管理页不会搜索 SkillHub / ClawHub；它只管理已经安装到本地的技能。

## 安全与信任

SkillHub / ClawHub 是外部来源。安装前建议阅读技能 README 和来源链接，确认它符合你的项目约束。

安装技能相当于把新的工作流说明加入工作区。对于不熟悉的技能，建议先在项目分支或受控工作区验证。即使市场搜索失败或网络不可用，本地技能搜索和管理仍可继续使用。

## 相关文档

- [Agent Skill 自学习](./agent-self-learning.md)
- [Skills Samples](../samples/skills.md)
