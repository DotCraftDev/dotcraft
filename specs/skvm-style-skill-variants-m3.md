# SkVM-Style Skill Variants M3 临时规范：Desktop Agent 安装体验闭环

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Temporary Draft |
| **Date** | 2026-05-01 |
| **Parent Spec** | [SkVM-Style Skill Variants](skvm-style-skill-variants.md) |
| **Depends On** | [M1 核心 Variant 闭环](skvm-style-skill-variants-m1.md), [M2 Skill 安装与 Agent 审阅](skvm-style-skill-variants-m2.md) |

> 临时说明：本文只用于阶段实现期间的任务拆分和验收。所有阶段完成后应删除本文，最终设计以 `skvm-style-skill-variants.md` 为准。

## 1. Overview

M3 将 M1/M2 的能力落到 Desktop 的可用产品体验中：Skill Market 保留现有的直接安装，同时增加一个 agent-assisted install 入口，让用户可以选择“让 Agent 安装并检查”。

该流程不向用户暴露 VM、variant、promotion、accept/reject 等内部概念。用户感知到的是：技能可以被普通安装，也可以让 Agent 在当前工作区里边安装、边阅读、边试用，并在发现环境差异时为当前工作区做必要调整。

M3 不再以 run records 或专业诊断面作为当前阶段目标。record、activity metadata、JIT optimization 可以在安装与 variant 基础体验稳定后再作为独立阶段设计。

## 2. Goal

- 完成 Desktop Skill Market 到 Agent 安装审阅的用户闭环。
- 让用户在不理解 SkVM/Variant 的前提下获得“安装后自动检查当前环境”的体验。
- 让 marketplace 下载、staging、agent install prompt、安装结果刷新形成可测试流程。
- 保持普通直接安装路径不变，降低行为变更风险。
- 为后续测试 Skill Variant 的真实价值提供一个稳定入口。

## 3. Scope

M3 包含：

- Desktop Skill Market 中新增 agent-assisted install CTA。
- 为 marketplace skill 准备 agent 可访问的安装来源：下载到 staging path，或在无法 staging 时提供 source URL。
- 打开一个专门的 Agent 技能安装界面、线程或引导任务。
- 自动生成安装审阅 prompt，要求 Agent 使用 `skill-installer` 从指定位置安装 skill。
- prompt 中明确要求 Agent 阅读 skill 内容，并根据当前工作区实际环境检查配置差异。
- 当 Agent 发现环境差异时，沿用 M1/M2 的 Skill Variant 写入能力做 workspace adaptation。
- 安装完成、需要用户处理、失败等结果状态在 Desktop 中可理解地呈现。
- Agent 安装完成后刷新 Skills 列表和当前 skill 状态。
- 覆盖关键 UI、IPC、prompt 生成和状态刷新测试。

M3 不包含：

- 删除或替换现有直接安装按钮。
- 用户可见的 variant diff、disable、accept、reject、promote、delete 工作流。
- run record、activity metadata、skill 使用频率统计或诊断面。
- 程序化 JIT/AOT optimizer。
- 程序硬编码判断 Python、shell、CLI 等环境兼容性。
- Skill Market 的整体信息架构重做。

## 4. User Experience Contract

### 4.1 Marketplace Actions

未安装的 marketplace skill 至少提供两个入口：

- `安装` / `Install`：保留现有直接安装语义。
- `让 Agent 安装并检查` / `Install with Agent`：进入 agent-assisted install 流程。

推荐 tooltip/help copy：

- 中文：`下载此技能，并让 Agent 按当前工作区安装、检查和必要时调整。`
- English: `Download this skill and let the agent install, check, and adapt it for this workspace.`

按钮 loading copy：

- 中文：`正在准备...`
- English: `Preparing...`

该入口面向普通用户，不出现 `variant`、`VM`、`source skill`、`promotion` 等术语。

### 4.2 Agent Install Surface

用户点击 `让 Agent 安装并检查` 后，Desktop 应进入一个额外界面、线程或引导安装面板。该界面表达的是“Agent 技能安装”，而不是内部 variant 管理。

该界面应包含：

- 当前 skill 名称、来源、版本。
- 安装来源状态：已下载到本地 staging path，或将从指定 URL 安装。
- Agent 将要执行的任务摘要。
- 可开始或已自动开始的 Agent 会话。
- 安装结果状态。

推荐页面标题：

- 中文：`Agent 技能安装`
- English: `Agent Skill Install`

推荐初始状态：

- 中文：`正在准备技能包...`
- English: `Preparing skill package...`

### 4.3 Result States

Desktop 至少应能表达以下结果：

- `已安装并检查`：Agent 完成安装和审阅，没有发现阻断问题。
- `已安装，需处理环境问题`：skill 已安装，但 Agent 发现缺少依赖、命令、运行环境或需要用户确认的配置。
- `安装未完成`：下载、staging、安装或 Agent 审阅失败。

如果 Agent 通过 variant 为当前工作区做了调整，UI 可以使用普通语言表达，例如：

- 中文：`已按当前工作区调整`
- English: `Adapted for this workspace`

UI 不应要求用户理解该调整来自 variant 文件，也不应提供 promotion 或 accept/reject 流程。

## 5. Required Workflow

### 5.1 Prepare Install Source

当用户从 Skill Market 选择 agent-assisted install：

1. Desktop 解析 provider、slug、version。
2. Desktop 下载或复制 marketplace skill package 到 agent 可访问的位置。
3. 推荐 staging root 位于 workspace 内，例如 `.craft/skill-install-staging/{provider}.{slug}.{timestamp}/`，以便 Agent 和工具都能稳定访问。
4. staging 内容应保留原始 skill package 结构，不在此阶段由程序做环境兼容性修改。
5. 如果 marketplace source 不能可靠 staging，Desktop 可退化为向 Agent 提供 source URL，但必须在界面中说明来源不是本地包。

staging 的清理策略可以是：

- 成功安装后允许延迟清理。
- 失败时保留短期可诊断内容。
- 后续启动或定期任务按 TTL 清理旧 staging 目录。

### 5.2 Start Agent Install Review

Desktop 应打开一个新的 Agent 安装上下文，并生成等价于以下语义的 prompt：

```text
请使用 skill-installer 从以下位置安装技能：
{stagedPathOrSourceUrl}

目标工作区：
{workspacePath}

安装后请阅读该技能的 SKILL.md 和它引用的必要文件，按技能自身内容检查它在当前工作区是否存在环境差异或使用冲突。

重点不是由程序判断 skill 是否“安全可用”，而是请你基于 skill 的说明和当前环境实际验证。例如：如果技能假设 Python 命令、Python 版本、虚拟环境、CLI 工具、shell、项目目录或配置文件存在，请按需检查。只有在发现具体差异时，才通过 SkillManage 为当前工作区写入必要的适配说明。

请最后用简短结果说明：
1. 是否完成安装。
2. 是否发现环境问题。
3. 是否已为当前工作区做了调整。
4. 用户是否还需要执行额外动作。
```

如果 Desktop 支持技能显式引用，prompt 应引用 `$skill-installer`。如果当前界面不能保证技能引用生效，prompt 文本必须包含足够明确的 `skill-installer` 使用要求。

### 5.3 Agent Responsibilities

Agent 在该流程中负责：

- 使用 `skill-installer` 执行安装。
- 阅读安装后的 skill 内容。
- 基于 skill 内容决定需要检查哪些环境事实。
- 在当前工作区执行必要的轻量验证。
- 发现真实冲突时，通过 M1/M2 的 variant-aware SkillManage 写入当前工作区适配。
- 不因为没有发现问题而制造 variant。
- 向用户报告清晰结果。

### 5.4 Program Responsibilities

程序在该流程中负责：

- 提供稳定的 staging path 或 source URL。
- 打开 Agent 安装界面或线程。
- 生成结构化、可复用的安装审阅 prompt。
- 提供 M1/M2 定义的 SkillView、SkillManage 和 restore original 能力。
- 在 Agent 安装结束后刷新 Skills 列表。
- 处理下载、staging、IPC、AppServer 连接和权限状态。

程序不负责：

- 根据 hardcoded 规则判断 skill 是否适配当前环境。
- 自动改写 skill 内容。
- 自动生成没有 Agent 审阅依据的 variant。

## 6. Compatibility Notes

- 直接安装路径必须保持现有行为。
- agent-assisted install 必须走 M2 定义的安装与 Agent 审阅语义。
- 如果 AppServer、Agent 会话或 SkillManage 不可用，`让 Agent 安装并检查` 应禁用或进入明确错误状态。
- 如果 self-learning/variant 写入能力被关闭，Agent 安装仍可检查问题，但 UI 必须说明无法保存当前工作区调整；推荐默认禁用该入口并引导用户启用对应能力。
- `SkillView` 仍只返回 effective `SKILL.md` 的正文内容，不暴露 variant metadata。
- Desktop copy 必须双语维护。

## 7. Acceptance Checklist

- [ ] Skill Market 未安装条目同时显示直接安装和 `让 Agent 安装并检查` 入口。
- [ ] 直接安装仍安装到 workspace skill 目录，行为与 M3 前一致。
- [ ] agent-assisted install 能准备本地 staging path 或提供 source URL。
- [ ] 点击 agent-assisted install 后打开 Agent 技能安装界面、线程或引导任务。
- [ ] 生成的 prompt 明确要求使用 `skill-installer` 从 staging path/source URL 安装。
- [ ] prompt 明确要求 Agent 按 skill 内容检查当前工作区环境差异。
- [ ] prompt 明确要求只有发现具体差异时才通过 SkillManage 写入 workspace adaptation。
- [ ] Agent 安装结束后 Desktop 刷新 Skills 列表和 skill 状态。
- [ ] UI 可表达已安装并检查、已安装但需处理环境问题、安装未完成。
- [ ] 普通用户界面不出现 VM、variant promotion、accept/reject/delete 等内部流程。
- [ ] AppServer、Agent 会话或 SkillManage 不可用时有清晰禁用或错误状态。
- [ ] 覆盖关键 UI copy、IPC request、prompt 生成和列表刷新测试。

## 8. Open Questions

M3 不保留必须先解决的设计问题。实际实现前只需要在实现方案中确认 Desktop 采用“新线程”“安装面板”还是“预填 composer”的具体承载方式。
