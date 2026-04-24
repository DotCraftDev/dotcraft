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
      { text: '文档索引', link: '/reference' },
      { text: '配置与安全', link: '/config_guide' },
      { text: 'Dashboard', link: '/dash_board_guide' },
      { text: '设置生效层级', link: '/settings-lifecycle' }
    ]
  },
  {
    text: '入口与协议',
    items: [
      { text: 'AppServer', link: '/appserver_guide' },
      { text: 'API 模式', link: '/api_guide' },
      { text: 'AG-UI 模式', link: '/agui_guide' },
      { text: 'ACP 模式', link: '/acp_guide' }
    ]
  },
  {
    text: '集成与自动化',
    items: [
      { text: 'Automations', link: '/automations_guide' },
      { text: 'Hooks', link: '/hooks_guide' },
      { text: 'External CLI 子代理', link: '/external_cli_subagents_guide' },
      { text: 'Unity 集成', link: '/unity_guide' },
      { text: 'QQ 机器人', link: '/qq_bot_guide' },
      { text: '企业微信', link: '/wecom_guide' }
    ]
  },
  {
    text: 'Samples',
    items: [
      { text: 'Samples 总览', link: '/samples/' },
      { text: 'AG-UI Client', link: '/samples/ag-ui-client' },
      { text: 'API Samples', link: '/samples/api' },
      { text: 'Automations Samples', link: '/samples/automations' },
      { text: 'Bootstrap Samples', link: '/samples/bootstrap' },
      { text: 'Hooks Samples', link: '/samples/hooks' },
      { text: 'Workspace Sample', link: '/samples/workspace' },
      { text: 'Skills Samples', link: '/samples/skills' }
    ]
  },
  {
    text: 'SDK',
    items: [
      { text: 'SDK 总览', link: '/sdk/' },
      { text: 'Python SDK', link: '/sdk/python' },
      { text: 'Python Telegram Adapter', link: '/sdk/python-telegram' },
      { text: 'TypeScript SDK', link: '/sdk/typescript' },
      { text: 'Feishu Adapter', link: '/sdk/typescript-feishu' },
      { text: 'Telegram Adapter', link: '/sdk/typescript-telegram' },
      { text: 'Weixin Adapter', link: '/sdk/typescript-weixin' }
    ]
  }
]

const enSidebar: DefaultTheme.Sidebar = [
  {
    text: 'Get Started',
    items: [
      { text: 'Documentation Index', link: '/en/reference' },
      { text: 'Configuration & Security', link: '/en/config_guide' },
      { text: 'Dashboard', link: '/en/dash_board_guide' },
      { text: 'Settings Lifecycle', link: '/en/settings-lifecycle' }
    ]
  },
  {
    text: 'Entry Points & Protocols',
    items: [
      { text: 'AppServer', link: '/en/appserver_guide' },
      { text: 'API Mode', link: '/en/api_guide' },
      { text: 'AG-UI Mode', link: '/en/agui_guide' },
      { text: 'ACP Mode', link: '/en/acp_guide' }
    ]
  },
  {
    text: 'Integrations & Automation',
    items: [
      { text: 'Automations', link: '/en/automations_guide' },
      { text: 'Hooks', link: '/en/hooks_guide' },
      { text: 'External CLI Subagents', link: '/en/external_cli_subagents_guide' },
      { text: 'Unity Integration', link: '/en/unity_guide' },
      { text: 'QQ Bot', link: '/en/qq_bot_guide' },
      { text: 'WeCom', link: '/en/wecom_guide' }
    ]
  },
  {
    text: 'Samples',
    items: [
      { text: 'Samples Overview', link: '/en/samples/' },
      { text: 'AG-UI Client', link: '/en/samples/ag-ui-client' },
      { text: 'API Samples', link: '/en/samples/api' },
      { text: 'Automations Samples', link: '/en/samples/automations' },
      { text: 'Bootstrap Samples', link: '/en/samples/bootstrap' },
      { text: 'Hooks Samples', link: '/en/samples/hooks' },
      { text: 'Workspace Sample', link: '/en/samples/workspace' },
      { text: 'Skills Samples', link: '/en/samples/skills' }
    ]
  },
  {
    text: 'SDK',
    items: [
      { text: 'SDK Overview', link: '/en/sdk/' },
      { text: 'Python SDK', link: '/en/sdk/python' },
      { text: 'Python Telegram Adapter', link: '/en/sdk/python-telegram' },
      { text: 'TypeScript SDK', link: '/en/sdk/typescript' },
      { text: 'Feishu Adapter', link: '/en/sdk/typescript-feishu' },
      { text: 'Telegram Adapter', link: '/en/sdk/typescript-telegram' },
      { text: 'Weixin Adapter', link: '/en/sdk/typescript-weixin' }
    ]
  }
]

const zhNav: DefaultTheme.NavItem[] = [
  { text: '功能', link: '/#features' },
  { text: '文档', link: '/reference' },
  { text: 'Samples', link: '/samples/' },
  {
    text: 'SDK',
    items: [
      { text: 'SDK 总览', link: '/sdk/' },
      { text: 'Python SDK', link: '/sdk/python' },
      { text: 'TypeScript SDK', link: '/sdk/typescript' },
      { text: '频道适配器', link: '/sdk/typescript-feishu' }
    ]
  }
]

const enNav: DefaultTheme.NavItem[] = [
  { text: 'Features', link: '/en/#features' },
  { text: 'Docs', link: '/en/reference' },
  { text: 'Samples', link: '/en/samples/' },
  {
    text: 'SDK',
    items: [
      { text: 'SDK Overview', link: '/en/sdk/' },
      { text: 'Python SDK', link: '/en/sdk/python' },
      { text: 'TypeScript SDK', link: '/en/sdk/typescript' },
      { text: 'Channel Adapters', link: '/en/sdk/typescript-feishu' }
    ]
  }
]

export default defineConfig({
  title: 'DotCraft',
  description: 'A project-scoped agent harness for persistent AI workspaces.',
  base,
  cleanUrls: true,
  ignoreDeadLinks: true,
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
