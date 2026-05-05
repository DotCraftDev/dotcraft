import { defineConfig, type DefaultTheme } from 'vitepress'

const repo = 'https://github.com/DotHarness/dotcraft'
const base = process.env.VITEPRESS_BASE ?? (process.env.GITHUB_ACTIONS ? '/dotcraft/' : '/')

function escapeMustaches(value: string): string {
  return value.replaceAll('{{', '&#123;&#123;').replaceAll('}}', '&#125;&#125;')
}

const zhSidebar: DefaultTheme.Sidebar = [
  {
    text: '开始使用',
    items: [
      { text: '快速开始', link: '/getting-started' }
    ]
  },
  {
    text: '日常使用',
    items: [
      { text: 'Desktop', link: '/desktop_guide' },
      { text: 'TUI', link: '/tui_guide' },
      { text: 'Agent Skill 自学习', link: '/skills/agent-self-learning' },
      { text: 'Skills 搜索与安装', link: '/skills/marketplace' },
      { text: 'Dashboard', link: '/dash_board_guide' }
    ]
  },
  {
    text: '插件',
    items: [
      { text: '安装和使用插件', link: '/plugins/install' },
      { text: '创建插件', link: '/plugins/build' }
    ]
  },
  {
    text: '配置与安全',
    items: [
      { text: '配置入门', link: '/config_guide' },
      { text: '安全配置', link: '/config/security' },
      { text: 'SubAgent 配置', link: '/subagents_guide' },
      { text: '设置生效层级', link: '/settings-lifecycle' },
      { text: '完整配置参考', link: '/reference/config' }
    ]
  },
  {
    text: '自动化工作流',
    items: [
      { text: 'Automations 入门', link: '/automations_guide' },
      { text: 'GitHub 自动化', link: '/automations/github' },
      { text: 'Automations 参考', link: '/automations/reference' },
      { text: 'Hooks 入门', link: '/hooks_guide' },
      { text: 'Hooks 参考', link: '/hooks/reference' },
      { text: 'External CLI 子代理', link: '/external_cli_subagents_guide' }
    ]
  },
  {
    text: '入口与协议',
    items: [
      { text: 'Hub', link: '/hub_guide' },
      { text: 'AppServer', link: '/appserver_guide' },
      { text: 'API 模式', link: '/api_guide' },
      { text: 'AG-UI 模式', link: '/agui_guide' },
      { text: 'ACP 模式', link: '/acp_guide' },
      { text: 'Unity 集成', link: '/unity_guide' }
    ]
  },
  {
    text: 'SDK 与渠道',
    items: [
      { text: 'SDK 总览', link: '/sdk/' },
      { text: 'Python SDK', link: '/sdk/python' },
      { text: 'Telegram (Python)', link: '/sdk/python-telegram' },
      { text: 'TypeScript SDK', link: '/sdk/typescript' },
      { text: '飞书', link: '/sdk/typescript-feishu' },
      { text: 'Telegram (TypeScript)', link: '/sdk/typescript-telegram' },
      { text: '微信', link: '/sdk/typescript-weixin' },
      { text: 'QQ', link: '/sdk/typescript-qq' },
      { text: '企业微信', link: '/sdk/typescript-wecom' }
    ]
  },
  {
    text: 'Samples',
    items: [
      { text: 'Samples 总览', link: '/samples/' },
      { text: 'AG-UI Client', link: '/samples/ag-ui-client' },
      { text: 'OpenAI API', link: '/samples/api' },
      { text: 'Automations', link: '/samples/automations' },
      { text: 'Bootstrap', link: '/samples/bootstrap' },
      { text: 'Hooks', link: '/samples/hooks' },
      { text: 'Workspace', link: '/samples/workspace' },
      { text: 'Skills', link: '/samples/skills' }
    ]
  },
  {
    text: '参考',
    items: [
      { text: '文档索引', link: '/reference' },
      { text: 'Hub Protocol', link: '/reference/hub-protocol' },
      { text: 'AppServer Protocol', link: '/reference/appserver-protocol' },
      { text: 'Dashboard API', link: '/reference/dashboard-api' },
      { text: 'TypeScript Module', link: '/typescript-module-integration' }
    ]
  }
]

const enSidebar: DefaultTheme.Sidebar = [
  {
    text: 'Get Started',
    items: [
      { text: 'Getting Started', link: '/en/getting-started' }
    ]
  },
  {
    text: 'Daily Use',
    items: [
      { text: 'Desktop', link: '/en/desktop_guide' },
      { text: 'TUI', link: '/en/tui_guide' },
      { text: 'Agent Skill Self-Learning', link: '/en/skills/agent-self-learning' },
      { text: 'Search and Install Skills', link: '/en/skills/marketplace' },
      { text: 'Dashboard', link: '/en/dash_board_guide' }
    ]
  },
  {
    text: 'Plugins',
    items: [
      { text: 'Install and Use Plugins', link: '/en/plugins/install' },
      { text: 'Build Plugins', link: '/en/plugins/build' }
    ]
  },
  {
    text: 'Configuration & Security',
    items: [
      { text: 'Configuration Basics', link: '/en/config_guide' },
      { text: 'Security Configuration', link: '/en/config/security' },
      { text: 'SubAgent Configuration', link: '/en/subagents_guide' },
      { text: 'Settings Lifecycle', link: '/en/settings-lifecycle' },
      { text: 'Full Configuration Reference', link: '/en/reference/config' }
    ]
  },
  {
    text: 'Automation Workflows',
    items: [
      { text: 'Automations Basics', link: '/en/automations_guide' },
      { text: 'GitHub Automations', link: '/en/automations/github' },
      { text: 'Automations Reference', link: '/en/automations/reference' },
      { text: 'Hooks Basics', link: '/en/hooks_guide' },
      { text: 'Hooks Reference', link: '/en/hooks/reference' },
      { text: 'External CLI Subagents', link: '/en/external_cli_subagents_guide' }
    ]
  },
  {
    text: 'Entry Points & Protocols',
    items: [
      { text: 'Hub', link: '/en/hub_guide' },
      { text: 'AppServer', link: '/en/appserver_guide' },
      { text: 'API Mode', link: '/en/api_guide' },
      { text: 'AG-UI Mode', link: '/en/agui_guide' },
      { text: 'ACP Mode', link: '/en/acp_guide' },
      { text: 'Unity Integration', link: '/en/unity_guide' }
    ]
  },
  {
    text: 'SDK & Channels',
    items: [
      { text: 'SDK Overview', link: '/en/sdk/' },
      { text: 'Python SDK', link: '/en/sdk/python' },
      { text: 'Telegram (Python)', link: '/en/sdk/python-telegram' },
      { text: 'TypeScript SDK', link: '/en/sdk/typescript' },
      { text: 'Feishu', link: '/en/sdk/typescript-feishu' },
      { text: 'Telegram (TypeScript)', link: '/en/sdk/typescript-telegram' },
      { text: 'Weixin', link: '/en/sdk/typescript-weixin' },
      { text: 'QQ', link: '/en/sdk/typescript-qq' },
      { text: 'WeCom', link: '/en/sdk/typescript-wecom' },
    ]
  },
  {
    text: 'Samples',
    items: [
      { text: 'Samples Overview', link: '/en/samples/' },
      { text: 'AG-UI Client', link: '/en/samples/ag-ui-client' },
      { text: 'OpenAI API', link: '/en/samples/api' },
      { text: 'Automations', link: '/en/samples/automations' },
      { text: 'Bootstrap', link: '/en/samples/bootstrap' },
      { text: 'Hooks', link: '/en/samples/hooks' },
      { text: 'Workspace', link: '/en/samples/workspace' },
      { text: 'Skills', link: '/en/samples/skills' }
    ]
  },
  {
    text: 'Reference',
    items: [
      { text: 'Documentation Index', link: '/en/reference' },
      { text: 'Hub Protocol', link: '/en/reference/hub-protocol' },
      { text: 'AppServer Protocol', link: '/en/reference/appserver-protocol' },
      { text: 'Dashboard API', link: '/en/reference/dashboard-api' },
      { text: 'TypeScript Module', link: '/en/typescript-module-integration' }
    ]
  }
]

const zhNav: DefaultTheme.NavItem[] = [
  { text: '功能', link: '/#features' },
  { text: '开始使用', link: '/getting-started' },
  { text: '文档索引', link: '/reference' },
  {
    text: 'SDK',
    items: [
      { text: 'SDK 总览', link: '/sdk/' },
      { text: 'Python SDK', link: '/sdk/python' },
      { text: 'Telegram (Python)', link: '/sdk/python-telegram' },
      { text: 'TypeScript SDK', link: '/sdk/typescript' },
      { text: '飞书', link: '/sdk/typescript-feishu' },
      { text: 'Telegram (TypeScript)', link: '/sdk/typescript-telegram' },
      { text: '微信', link: '/sdk/typescript-weixin' },
      { text: 'QQ', link: '/sdk/typescript-qq' },
      { text: '企业微信', link: '/sdk/typescript-wecom' }
    ]
  }
]

const enNav: DefaultTheme.NavItem[] = [
  { text: 'Features', link: '/en/#features' },
  { text: 'Get Started', link: '/en/getting-started' },
  { text: 'Docs Index', link: '/en/reference' },
  {
    text: 'SDK',
    items: [
      { text: 'SDK Overview', link: '/en/sdk/' },
      { text: 'Python SDK', link: '/en/sdk/python' },
      { text: 'Telegram (Python)', link: '/en/sdk/python-telegram' },
      { text: 'TypeScript SDK', link: '/en/sdk/typescript' },
      { text: 'Feishu', link: '/en/sdk/typescript-feishu' },
      { text: 'Telegram (TypeScript)', link: '/en/sdk/typescript-telegram' },
      { text: 'Weixin', link: '/en/sdk/typescript-weixin' },
      { text: 'QQ', link: '/en/sdk/typescript-qq' },
      { text: 'WeCom', link: '/en/sdk/typescript-wecom' }
    ]
  }
]

export default defineConfig({
  title: 'DotCraft',
  description: 'A project-scoped agent harness for persistent AI workspaces.',
  base,
  cleanUrls: true,
  lastUpdated: true,
  head: [
    ['meta', { name: 'theme-color', content: '#4A7FA5' }],
    ['link', { rel: 'icon', href: `${base}dotcraft-logo.svg` }]
  ],
  markdown: {
    image: {
      lazyLoading: true
    },
    config(md) {
      const defaultFence = md.renderer.rules.fence
      const defaultCodeBlock = md.renderer.rules.code_block

      md.renderer.rules.text = (tokens, idx) => escapeMustaches(md.utils.escapeHtml(tokens[idx].content))

      md.renderer.rules.code_inline = (tokens, idx) =>
        `<code>${escapeMustaches(md.utils.escapeHtml(tokens[idx].content))}</code>`

      md.renderer.rules.fence = (tokens, idx, options, env, self) =>
        escapeMustaches(
          defaultFence
            ? defaultFence(tokens, idx, options, env, self)
            : self.renderToken(tokens, idx, options)
        )

      md.renderer.rules.code_block = (tokens, idx, options, env, self) =>
        escapeMustaches(
          defaultCodeBlock
            ? defaultCodeBlock(tokens, idx, options, env, self)
            : `<pre><code>${md.utils.escapeHtml(tokens[idx].content)}</code></pre>\n`
        )
    }
  },
  themeConfig: {
    logo: '/dotcraft-logo.svg',
    siteTitle: 'DotCraft',
    search: { provider: 'local' },
    socialLinks: [{ icon: 'github', link: repo }],
    editLink: {
      pattern: `${repo}/edit/master/docs/:path`,
      text: 'Edit this page on GitHub'
    },
    footer: {
      message: 'Apache License 2.0',
      copyright: 'Copyright © DotCraft contributors'
    }
  },
  locales: {
    root: {
      label: '简体中文',
      lang: 'zh-CN',
      title: 'DotCraft',
      description: '面向项目的 Agent Harness，打造持久的 AI 工作空间。',
      themeConfig: {
        nav: zhNav,
        sidebar: zhSidebar,
        outline: { label: '本页目录' },
        docFooter: {
          prev: '上一页',
          next: '下一页'
        },
        lastUpdated: {
          text: '最后更新'
        },
        langMenuLabel: '语言',
        returnToTopLabel: '回到顶部',
        sidebarMenuLabel: '菜单',
        darkModeSwitchLabel: '外观',
        lightModeSwitchTitle: '切换到浅色模式',
        darkModeSwitchTitle: '切换到深色模式'
      }
    },
    en: {
      label: 'English',
      lang: 'en-US',
      title: 'DotCraft',
      description: 'A project-scoped agent harness for persistent AI workspaces.',
      themeConfig: {
        nav: enNav,
        sidebar: enSidebar,
        outline: { label: 'On this page' },
        editLink: {
          pattern: `${repo}/edit/master/docs/:path`,
          text: 'Edit this page on GitHub'
        }
      }
    }
  }
})
