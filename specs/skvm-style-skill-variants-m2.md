# SkVM-Style Skill Variants M2 临时规范：Skill 安装与 Agent 审阅

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Temporary Draft |
| **Date** | 2026-05-01 |
| **Parent Spec** | [SkVM-Style Skill Variants](skvm-style-skill-variants.md) |
| **Depends On** | [M1 核心 Variant 闭环](skvm-style-skill-variants-m1.md) |

> 临时说明：本文只用于阶段实现期间的任务拆分和验收。所有阶段完成后应删除本文，最终设计以 `skvm-style-skill-variants.md` 为准。

## 1. Overview

M2 增加 agent-assisted skill 安装与导入流程。用户仍可手动放置 skill 文件夹，但当用户让 agent 安装或导入 skill 时，DotCraft 应提供候选目录校验、source 安装、source fingerprint 计算和 variant 写入能力；真正判断 skill 与当前用户环境是否冲突、是否需要调整，以及如何调整，应由 agent 在安装审阅和后续使用中完成。

M2 的目标是让导入 skill 的安全边界和 workspace adaptation 从一开始就建立，同时保留 agent 通过真实环境观察来学习和修正 skill 的能力。

M2 不做程序化 AOT/JIT optimizer，也不由程序硬编码判断 Python、shell、虚拟环境或工具链兼容性。程序只提供导入校验、原子发布、隔离、回退和 variant 写入机制；agent 负责下载/整理候选 skill、阅读 skill、检查当前环境、发现冲突，并把修正写入 workspace-local variant。

## 2. Goal

- 保留手动安装 skill 的简单路径。
- 增加内置 `skill-installer` 工作流，让 agent 可通过普通 shell/文件操作准备 catalog/GitHub/local skill 候选目录，并调用 DotCraft CLI 校验安装。
- 导入后由 agent 审阅 source skill，并在发现当前环境冲突或用户工作流差异时创建/更新当前 workspace 的 variant。
- 用户只感知“skill 已安装并可用”，不需要理解内部适配或 variant。

## 3. Scope

M2 包含：

- 内置 `skill-installer` skill。
- 安装服务的行为契约：verify candidate directory、install verified candidate directory。
- 安装后的 source skill 校验。
- 安装后的 agent review 与 variant learning 流程。
- 安装失败、校验失败、variant 生成失败时的用户可理解反馈。
- DotCraft CLI 的 `skill verify` / `skill install` 子命令，供 agent workflow 和后续 Desktop 预下载流程复用。

M2 不包含：

- marketplace 产品 UI。
- 私有 registry 的权限管理体系。
- 复杂依赖解析或多 skill bundle 安装事务。
- benchmark 级别的 SkVM primitive profiling。
- run records/activity metadata 的完整生命周期。

## 4. Behavioral Contract

### 4.1 Manual Install

- 用户手动将 skill 放到 `.craft/skills/{name}/` 或 user skill root 时，现有 loader 行为不变。
- 手动安装不会强制触发 agent review。
- 手动安装的 skill 在首次 `SkillView` 或 `SkillManage` 使用时仍可走 M1 的 resolver/fork 路径。

### 4.2 Agent-Assisted Install

内置 `skill-installer` 应支持：

- 从用户提供的 local path 验证并安装 skill。
- 从 GitHub repo/path、URL、archive 或 market 预下载位置整理出候选 skill 目录。
- 在候选目录中按需修正 `SKILL.md` 或 supporting files，使其成为合法 DotCraft skill。
- 通过 DotCraft CLI 执行 verify/install。
- 已存在同名 source skill 时拒绝覆盖，除非用户明确要求替换。

M2 不暴露专用的 `SkillInstall` agent tool。agent 使用普通 `Exec`、文件读取/编辑和 shell/git/download 能力准备候选目录，然后调用：

```powershell
dotcraft skill verify --candidate "<candidate-dir>" --json
dotcraft skill install --candidate "<candidate-dir>" --source "<where this came from>" --json
```

`--overwrite` 只在用户明确要求替换当前 workspace source skill 时使用。

### 4.3 Import Package Validation

程序必须只做基础包结构校验，确保导入物可以作为 DotCraft source skill 被安全保存：

- skill name 合法。
- `SKILL.md` 存在且 frontmatter 合法。
- `name` 与目标目录一致。
- `description` 存在。
- supporting files 不违反 path traversal 和允许目录约束。
- source skill 不超过配置的大小限制。

验证失败时：

- 不应留下半安装的 source skill。
- 向用户给出简洁错误说明。
- 不创建 variant。

### 4.4 Import Processing Pipeline

agent-assisted install 必须按以下顺序处理 skill：

1. **Candidate preparation**：agent 先下载、复制、解压或 clone 到 workspace 临时候选目录，不直接写入 `.craft/skills/`。
2. **Candidate repair**：如果导入物不是合法 DotCraft skill，agent 可在候选目录内做必要整理或编辑。
3. **Package validation**：通过 `dotcraft skill verify --candidate ... --json` 完成 Section 4.3 的基础结构、安全和大小校验。
4. **Source install**：通过 `dotcraft skill install --candidate ... --json` 将校验通过的候选 bundle 原子安装为 source skill。
5. **Fingerprint**：安装时计算 source fingerprint，作为后续 variant 绑定依据。
6. **Agent review**：agent 读取该 skill 的 effective body，并根据 skill 自身内容决定需要检查哪些环境事实。
7. **Variant learning**：如果 agent 发现 source skill 与当前环境、工具、命令、依赖或用户工作流存在冲突，它通过 M1 的 variant-aware `SkillManage` 写入 current variant。
8. **Fallback**：agent review 或 variant 写入失败时，source skill 仍安装成功，resolver 回退 source skill。

### 4.5 Agent Review

Agent review 是 M2 的核心处理步骤。它不是程序化扫描，也不是固定规则引擎。

Agent review 必须遵循：

- agent 先阅读新安装 skill 的 effective body。
- agent 根据 skill 内容决定是否需要检查环境。例如 skill 提到 Python 时，agent 可以检查 `python`、`python3`、`pyenv` 或项目虚拟环境；skill 提到特定 CLI 时，agent 可以检查对应命令是否可用。
- agent 的检查必须尽量低风险、可解释，并遵守现有工具审批、shell、文件和 sandbox 策略。
- agent 不需要证明整个 skill 一定可用；它只需要发现明显会影响当前 workspace 使用的冲突。
- 如果没有发现冲突，agent 不创建 variant，source skill 直接可用。
- 如果发现冲突，agent 应把“当前 workspace 该如何使用此 skill”的经验写入 variant，而不是修改 source skill。

Agent review 的典型发现：

- skill 文档要求 `python`，但当前环境只有 `python3`。
- skill 假设全局 Python，而 workspace 使用 `.venv` 或 `pyenv`。
- skill 示例使用 bash，但当前主 shell 是 PowerShell。
- skill 使用的工具名与 DotCraft 当前可用工具名不同。
- skill 要求的 supporting file 路径和实际 bundle 内容不一致。

### 4.6 Variant Learning During Install

安装阶段创建的 variant 是 agent 根据实际检查结果写下的 workspace-specific 修正。

variant learning 必须满足：

- 完整复制 source skill bundle，source skill 本身不被修改。
- 保留原 skill 的任务意图和主体步骤。
- 只记录 agent 在当前环境中真实观察到或合理确认的差异。
- 不为未验证的问题编造适配规则。
- 不把“安装时没有检查到的问题”写成已解决。
- 生成后状态为 current，后续 self-learning 可在 M1 的 `SkillManage` variant 路径上继续迭代。

示例：如果 skill 要求 Python，而 agent 发现当前 workspace 应使用 `.venv/Scripts/python.exe` 或 `python3`，variant 可以补充该 workspace 的 Python 调用方式、验证命令和失败排查步骤。

最终用户不应看到 variant learning 的内部名称。普通反馈仍是“skill 已安装并可用”；如果发现需要用户处理的依赖缺失，反馈应说明缺少什么以及 skill 仍已安装。

## 5. Compatibility Notes

- M2 不改变 M1 的 source/variant resolution 规则。
- 安装服务不应绕过现有文件安全策略。
- `skill verify` 不要求 workspace 已配置 API key；`skill install` 要求当前目录是 DotCraft workspace。
- 下载或复制得到的 source skill 应记录 provenance，但 provenance 不应暴露给普通 agent prompt。
- Windows/PowerShell、Python/pyenv/venv、CLI 名称等差异应由 agent 在 review 或后续使用中发现，并通过 variant 学习记录，不能要求用户手动修改 source skill。

## 6. Acceptance Checklist

- [ ] 用户仍可通过手动放置目录安装 skill。
- [ ] agent 可通过 `skill-installer` 安装一个本地路径 skill。
- [ ] agent 可通过普通 shell/git/download 操作准备 GitHub/path skill 候选目录，并通过 `skill-installer` 完成校验安装。
- [ ] 同名 skill 默认拒绝覆盖。
- [ ] 缺失或非法 frontmatter 的 skill 不会被安装。
- [ ] `dotcraft skill verify --candidate ... --json` 可独立校验候选目录。
- [ ] `dotcraft skill install --candidate ... --json` 基础包校验通过后才写入 source skill root。
- [ ] 安装成功后 source fingerprint 可计算。
- [ ] agent review 会读取新安装 skill，并根据 skill 内容决定需要检查的环境事实。
- [ ] 当 agent 发现 Python、shell、CLI、虚拟环境或 supporting file 冲突时，会通过 variant-aware `SkillManage` 写入 current variant。
- [ ] 没有发现冲突时，不强制创建 variant。
- [ ] agent review 或 variant 写入失败不会导致 source skill 安装失败。
- [ ] 安装成功后的用户提示不暴露 VM/variant 细节。

## 7. Open Questions

M2 不保留产品级 open question。catalog 来源、认证方式和 marketplace 体验若需要扩展，应另开后续 spec。
