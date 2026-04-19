# DotCraft 设置生效层级指南

**中文 | [English](./en/settings-lifecycle.md)**

本文档说明 Desktop 设置中的三层生效模型、API Proxy 对 LLM 字段的锁定行为，以及如何判断配置是已生效还是待生效。

## 1. 三层生效模型

Desktop 将设置项按生效方式划分为三类：

1. **即时生效（Tier A / Live Apply）**
   - 保存后立即生效。
   - 典型示例：`Skills.DisabledSkills`、MCP 配置项。
2. **子系统重启（Tier B / Subsystem Restart）**
   - 配置已写入，但需要重启对应子系统后生效。
   - 典型示例：受代理与通道子系统生命周期影响的配置。
3. **AppServer 重启（Tier C / Process Restart）**
   - 配置已写入，但需要重启 AppServer 进程后生效。
   - 典型示例：`Core.ApiKey`、`Core.EndPoint`、`Core.Model`。

你可以通过设置分组中的动作按钮识别层级：即时应用、重启、或“应用并重启”。

## 2. 代表字段与生效方式

| 配置区域 | 代表字段 | 生效方式 |
|---|---|---|
| Skills / MCP | `Skills.DisabledSkills`、MCP 服务器定义 | 即时生效 |
| Proxy / External Channel | 代理与外部通道相关配置 | 子系统重启 |
| Core (LLM) | `ApiKey`、`EndPoint`、`Model` | AppServer 重启 |

说明：

- Desktop 的 LLM 设置页当前展示 `ApiKey` 与 `EndPoint`。
- `Model` 仍属于进程重启语义，但已从 Desktop 的该分组编辑入口移除。

## 3. API Proxy 锁定行为

当托管 API Proxy 处于运行状态时：

- `ApiKey` 和 `EndPoint` 会被锁定为代理管理值。
- 这两个输入项不可直接编辑。
- 若需要恢复手动编辑：先关闭 Proxy，再执行对应的应用/重启动作，使运行态与配置态对齐。

## 4. 如何判断“已生效”与“待生效”

可以从以下信号判断状态：

- **已生效**：配置已写入且对应层级动作已完成（即时应用成功，或重启完成）。
- **待生效**：出现需要重启的提示，表示配置已落盘但运行态尚未切换。
- **按分组脏状态**：仅变更了某一分组时，只需要处理该分组对应动作，不必全局保存。

## 5. 迁移与版本说明

本系列变更（M1-M4）对用户的主要影响：

- 设置界面从统一底部 Save/Cancel 迁移为按分组动作（Apply / Restart / Apply & Restart）。
- LLM 分组强化了 `ApiKey` / `EndPoint` 与 Proxy 的联动锁定语义。
- AppServer 新增 `workspace/configChanged` 通知，客户端可按 region 增量刷新相关数据。

### 常见问题

**问：我改了 Model，为什么没有立即生效？**  
答：`Model` 属于 Tier C（进程重启）。写入配置后需要重启 AppServer 才会反映到运行态。

协议细节请参阅 [AppServer Protocol](../specs/appserver-protocol.md) 的 `workspace/configChanged` 章节。
