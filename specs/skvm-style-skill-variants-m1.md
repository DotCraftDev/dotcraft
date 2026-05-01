# SkVM-Style Skill Variants M1 临时规范：核心 Variant 闭环

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Temporary Draft |
| **Date** | 2026-05-01 |
| **Parent Spec** | [SkVM-Style Skill Variants](skvm-style-skill-variants.md) |

> 临时说明：本文只用于阶段实现期间的任务拆分和验收。所有阶段完成后应删除本文，最终设计以 `skvm-style-skill-variants.md` 为准。

## 1. Overview

M1 交付最小可用的 SkVM-style skill variant 闭环：DotCraft 能在内部区分 source skill 与 workspace-local variant，agent 通过 `SkillView` 读取 effective skill，自学习修改默认写入 variant，用户可以一键恢复原始技能。

M1 不追求自动优化质量，只建立安全隔离和读取/写入路径。

## 2. Goal

- 保护 source skill，不让 agent self-learning 直接修改原始技能。
- 让 agent 无感读取 effective skill，不暴露 VM/variant 细节。
- 让现有 `SkillManage` 的日常更新能力迁移到 variant。
- 提供用户可理解的恢复动作：恢复原始技能。

## 3. Scope

M1 包含：

- workspace-local variant 存储和 manifest。
- source fingerprint 与 target signature 的基础解析。
- effective skill resolver。
- agent-facing `SkillView(name)` 工具。
- variant-aware `SkillManage` 行为。
- `skills/view` 与 `skills/restoreOriginal` AppServer 方法。
- prompt 中引导 agent 使用 `SkillView` 加载 skill。

M1 不包含：

- skill 安装器和 GitHub/catalog 导入。
- run records 和 activity metadata 的完整写入。
- UI 中的使用统计或诊断面。
- 自动 AOT/JIT 优化器。
- GraSP 规划或 graph execution。

## 4. Behavioral Contract

### 4.1 Effective Skill Resolution

- 当没有可用 variant 时，effective skill 等于 source skill。
- 当存在匹配当前 workspace/model/tool/runtime 的 current variant 时，effective skill 使用 variant 内容。
- 当 source fingerprint 变化时，旧 variant 视为 stale，resolver 回退 source skill。
- 当用户执行恢复原始技能后，resolver 回退 source skill，并且不能在没有新证据的情况下重新选择同一个 restored variant。

### 4.2 SkillView

- `SkillView(name)` 返回 effective `SKILL.md` 正文纯文本。
- 返回内容默认剥离 YAML frontmatter。
- 不返回 variant id、source path、effective path、fingerprint、origin 或 supporting file 列表。
- skill 正文中引用的 `scripts/` / `assets/` 文件由 agent 通过普通文件工具按需读取。

### 4.3 SkillManage

- `create` 创建新的 workspace source skill，并保持现有审批语义。
- `delete` 删除 workspace source skill，并保持现有审批语义。
- `edit`、`patch`、`write_file`、`remove_file` 在 variant mode enabled 时写入 current variant。
- 如果还没有 current variant，第一次 mutation 必须从当前 source skill bundle fork 一个 variant 后再修改。
- mutation 成功消息可以说明原始技能未被修改，但不能向 agent 暴露 variant id 或 source fingerprint。

### 4.4 Restore Original Skill

- 恢复原始技能只影响 variant 选择，不修改 source skill。
- 恢复后应保留轻量 tombstone，防止 resolver 无新证据时立即重新选择同一 variant。
- 大型 variant snapshot 可由后续 GC 清理，但 restored 状态必须可被 resolver 识别。

## 5. Compatibility Notes

- `skills/read` 保持 source-oriented 兼容语义。
- 现有 `SKILL.md` 文件继续可用。
- `Skills.SelfLearning.Enabled=false` 时不暴露 `SkillManage`，但仍应允许 `SkillView` 读取 effective skill。
- `VariantMode=disabled` 时系统退回 source-only 行为。
- 现有 built-in/user/workspace skill resolution 优先级不改变。

## 6. Acceptance Checklist

- [ ] `SkillView(name)` 能读取 source skill body，并剥离 frontmatter。
- [ ] 有 current variant 时，`SkillView(name)` 返回 variant body。
- [ ] `SkillView` 不泄露 variant 元数据。
- [ ] `SkillManage(patch/edit/write_file/remove_file)` 不修改 source skill。
- [ ] 第一次 self-learning mutation 自动 fork variant。
- [ ] source fingerprint 变化后旧 variant 不再被选中。
- [ ] restore original 后 `SkillView` 回到 source body。
- [ ] `skills/view` 返回 effective body。
- [ ] `skills/read` 仍返回 source raw content。
- [ ] self-learning disabled 时 `SkillManage` 不暴露，`SkillView` 仍暴露。

## 7. Open Questions

M1 不保留产品级 open question。若实现中发现必须改变 `skvm-style-skill-variants.md` 的决策，应先回到主 spec 讨论。

