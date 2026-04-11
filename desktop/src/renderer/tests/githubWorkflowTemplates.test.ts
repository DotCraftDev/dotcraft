import { describe, expect, it } from 'vitest'
import {
  buildGitHubWorkflowTemplate,
  buildWorkflowCopyPath,
  resolveWorkflowAbsolutePath
} from '../components/automations/githubWorkflowTemplates'

describe('github workflow templates', () => {
  it('builds a PR workflow with selected review settings', () => {
    const output = buildGitHubWorkflowTemplate({
      kind: 'pullRequest',
      path: 'PR_WORKFLOW.md',
      maxTurns: 12,
      concurrency: 3,
      beforeRunHook: '',
      reviewStyle: 'strict',
      issueWorkMode: 'plan-implement-pr',
      activeIssueStates: ['Todo', 'In Progress']
    })

    expect(output).toContain('max_turns: 12')
    expect(output).toContain('max_concurrent_pull_request_agents: 3')
    expect(output).toContain('Use a strict bar.')
    expect(output).toContain('SubmitStructuredReview')
    expect(output).toContain('No issues found.')
  })

  it('builds an issue workflow in plan-only mode', () => {
    const output = buildGitHubWorkflowTemplate({
      kind: 'issue',
      path: 'WORKFLOW.md',
      maxTurns: 8,
      concurrency: 2,
      beforeRunHook: '',
      reviewStyle: 'balanced',
      issueWorkMode: 'plan-only',
      activeIssueStates: ['Todo', 'In Progress']
    })

    expect(output).toContain('active_states: ["Todo", "In Progress"]')
    expect(output).toContain('max_turns: 8')
    expect(output).toContain('Plan-only mode')
    expect(output).toContain('Do not start coding in this run.')
  })

  it('resolves workflow paths safely for renderer usage', () => {
    expect(resolveWorkflowAbsolutePath('F:/repo', 'foo/bar.md')).toBe('F:/repo/foo/bar.md')
    expect(buildWorkflowCopyPath('flows/PR_WORKFLOW.md')).toBe('flows/PR_WORKFLOW.template-copy.md')
  })
})
