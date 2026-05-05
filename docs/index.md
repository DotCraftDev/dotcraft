---
layout: page
title: DotCraft
description: 面向项目的 Agent Harness，打造持久的 AI 工作空间。
aside: false
sidebar: false
editLink: false
lastUpdated: false
---

<div class="dc-home">
  <section class="dc-hero" style="--hero-image: url('https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png')">
    <div class="dc-hero__content">
      <p class="dc-kicker">Unified Session Core · .NET 10 · Agent Harness</p>
      <h1>围绕项目构建持久 AI 工作空间</h1>
      <p class="dc-hero__lead">
        DotCraft 将 CLI、Desktop、IDE、聊天机器人、API 与自动化任务接入同一个工作区，让会话、记忆、技能和工具在不同入口之间保持一致。
      </p>
      <div class="dc-actions">
        <a class="dc-button dc-button--primary" href="./getting-started">开始使用</a>
        <a class="dc-button" href="https://github.com/DotHarness/dotcraft/releases">下载 Release</a>
        <a class="dc-button" href="./config_guide">完整配置</a>
        <a class="dc-button" href="https://github.com/DotHarness/dotcraft">GitHub</a>
      </div>
    </div>
  </section>

  <section id="features" class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>一个核心，多种入口</h2>
        <p class="dc-section__text">
          DotCraft 不是为每个客户端维护一套独立 agent 流程，而是用统一会话核心承接执行、状态、审批与可观测性。
        </p>
      </div>
      <div class="dc-grid">
        <article class="dc-card">
          <h3>项目级工作区</h3>
          <p>围绕真实项目目录组织会话、记忆、技能、配置和任务，让 agent 能持续理解你的项目。</p>
        </article>
        <article class="dc-card">
          <h3>统一会话模型</h3>
          <p>Thread、Turn、Item 模型跨 CLI、Desktop、ACP、QQ、WeCom 和自动化任务复用。</p>
        </article>
        <article class="dc-card">
          <h3>可观测与治理</h3>
          <p>审批、工具调用、Trace、Dashboard 和沙箱隔离让 agent 工作流更容易检查、恢复和约束。</p>
        </article>
      </div>
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner dc-shot">
      <div>
        <p class="dc-kicker">Desktop first</p>
        <h2>图形化管理会话、Diff、计划与自动化</h2>
        <p class="dc-section__text">
          Desktop 是推荐的第一入口。先完成下载、工作区初始化和模型配置，再按需进入 TUI、AppServer、ACP、频道或自动化。
        </p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">5 分钟快速开始</a>
          <a class="dc-button" href="./desktop_guide">Desktop 指南</a>
          <a class="dc-button" href="./tui_guide">TUI 指南</a>
        </div>
      </div>
      <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png" alt="DotCraft Desktop" />
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>面向集成的协议与 SDK</h2>
        <p class="dc-section__text">
          AppServer 通过 JSON-RPC over stdio/WebSocket 暴露统一能力，外部客户端、频道适配器和 SDK 可以复用同一套运行时。
        </p>
      </div>
      <div class="dc-link-list">
        <a href="./appserver_guide">AppServer <span>无头服务、远程 CLI、多客户端共享工作区。</span></a>
        <a href="./api_guide">OpenAI-compatible API <span>作为 HTTP 服务暴露模型与工具能力。</span></a>
        <a href="./agui_guide">AG-UI <span>通过 SSE 接入 CopilotKit 等前端 agent UI。</span></a>
        <a href="./acp_guide">ACP <span>连接 JetBrains、Obsidian、Unity 等编辑器和 IDE。</span></a>
      </div>
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner dc-shot">
      <div>
        <p class="dc-kicker">Automations</p>
        <h2>把 agent 工作流放进任务管线</h2>
        <p class="dc-section__text">
          本地任务和 Cron 在工作区内提供调度、线程绑定、活动展示和重试能力。
        </p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./automations_guide">查看 Automations</a>
          <a class="dc-button" href="./hooks_guide">扩展 Hooks</a>
        </div>
      </div>
      <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_automations.png" alt="DotCraft automations panel" />
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>三步开始</h2>
        <p class="dc-section__text">第一次使用请从 Desktop 开始。跑通之后，同一工作区可以继续接入终端、编辑器、API、SDK 和自动化任务。</p>
      </div>
      <div class="dc-steps">
        <div class="dc-step"><div><strong>下载 Desktop</strong><span>从 Release 安装桌面应用，或从源码构建后启动 Desktop。</span></div></div>
        <div class="dc-step"><div><strong>选择项目文件夹</strong><span>选择真实项目目录，让配置、会话和任务跟随这个项目保存。</span></div></div>
        <div class="dc-step"><div><strong>配置模型并开始对话</strong><span>设置 OpenAI-compatible API Key 或 CLIProxyAPI，发送第一次仓库理解请求。</span></div></div>
      </div>
    </div>
  </section>
</div>
