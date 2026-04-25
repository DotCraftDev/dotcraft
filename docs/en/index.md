---
layout: page
title: DotCraft
description: A project-scoped agent harness for persistent AI workspaces.
aside: false
sidebar: false
editLink: false
lastUpdated: false
---

<div class="dc-home">
  <section class="dc-hero" style="--hero-image: url('https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png')">
    <div class="dc-hero__content">
      <p class="dc-kicker">Unified Session Core · .NET 10 · Agent Harness</p>
      <h1>Craft a persistent AI workspace around your project</h1>
      <p class="dc-hero__lead">
        DotCraft connects CLI, Desktop, IDEs, chat bots, APIs, and automations to one workspace so sessions, memory, skills, and tools stay coherent across every entry point.
      </p>
      <div class="dc-actions">
        <a class="dc-button dc-button--primary" href="./getting-started">Get started</a>
        <a class="dc-button" href="https://github.com/DotHarness/dotcraft/releases">Download release</a>
        <a class="dc-button" href="./config_guide">Full configuration</a>
        <a class="dc-button" href="https://github.com/DotHarness/dotcraft">GitHub</a>
      </div>
    </div>
  </section>

  <section id="features" class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>One core, many entry points</h2>
        <p class="dc-section__text">
          DotCraft does not keep separate agent loops for each client. The Unified Session Core owns execution, state, approvals, and observability.
        </p>
      </div>
      <div class="dc-grid">
        <article class="dc-card">
          <h3>Project-scoped workspace</h3>
          <p>Sessions, memory, skills, configuration, and tasks are organized around the real project folder.</p>
        </article>
        <article class="dc-card">
          <h3>Unified session model</h3>
          <p>The Thread, Turn, and Item model is shared by CLI, Desktop, ACP, QQ, WeCom, GitHub Tracker, and automations.</p>
        </article>
        <article class="dc-card">
          <h3>Observability and governance</h3>
          <p>Approvals, tool calls, traces, Dashboard, and sandbox isolation make agent workflows easier to inspect and control.</p>
        </article>
      </div>
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner dc-shot">
      <div>
        <p class="dc-kicker">Desktop first</p>
        <h2>Manage sessions, diffs, plans, and automations visually</h2>
        <p class="dc-section__text">
          Desktop is the recommended first entry point. Start with download, workspace initialization, and model configuration, then enable TUI, AppServer, ACP, channels, or automations as needed.
        </p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./getting-started">5-minute start</a>
          <a class="dc-button" href="./desktop_guide">Desktop guide</a>
          <a class="dc-button" href="./tui_guide">TUI guide</a>
        </div>
      </div>
      <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png" alt="DotCraft Desktop" />
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>Protocols and SDKs for integration</h2>
        <p class="dc-section__text">
          AppServer exposes DotCraft over JSON-RPC via stdio or WebSocket, so external clients, channel adapters, and SDKs can reuse the same runtime.
        </p>
      </div>
      <div class="dc-link-list">
        <a href="./appserver_guide">AppServer <span>Headless service, remote CLI, and shared workspaces.</span></a>
        <a href="./api_guide">OpenAI-compatible API <span>Expose model and tool capabilities as an HTTP service.</span></a>
        <a href="./agui_guide">AG-UI <span>Connect frontend agent UIs such as CopilotKit through SSE.</span></a>
        <a href="./acp_guide">ACP <span>Connect editors and IDEs including JetBrains, Obsidian, and Unity.</span></a>
      </div>
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner dc-shot">
      <div>
        <p class="dc-kicker">Automations</p>
        <h2>Put agent work into a task pipeline</h2>
        <p class="dc-section__text">
          Local tasks, Cron, and GitHub Issue/PR tracking share one orchestrator with scheduling, concurrency control, human review, and requeue behavior.
        </p>
        <div class="dc-actions">
          <a class="dc-button dc-button--primary" href="./automations_guide">View Automations</a>
          <a class="dc-button" href="./hooks_guide">Extend with Hooks</a>
        </div>
      </div>
      <img src="https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_github.png" alt="DotCraft automations panel" />
    </div>
  </section>

  <section class="dc-section">
    <div class="dc-section__inner">
      <div class="dc-section__header">
        <h2>Start in three steps</h2>
        <p class="dc-section__text">For first-time use, start from Desktop. Once that works, the same workspace can connect terminal, editor, API, SDK, and automation entry points.</p>
      </div>
      <div class="dc-steps">
        <div class="dc-step"><div><strong>Download Desktop</strong><span>Install a release build, or build from source and start Desktop.</span></div></div>
        <div class="dc-step"><div><strong>Choose a project folder</strong><span>Select a real project folder so configuration, sessions, and tasks stay with that project.</span></div></div>
        <div class="dc-step"><div><strong>Configure a model and chat</strong><span>Set an OpenAI-compatible API key or CLIProxyAPI, then send your first repository-understanding request.</span></div></div>
      </div>
    </div>
  </section>
</div>
