import { describe, expect, it } from 'vitest'
import {
  formatSkillViewLabel,
  formatSkillViewRunningLabel,
  getSkillViewDisplay
} from '../utils/skillViewToolDisplay'

describe('skillViewToolDisplay', () => {
  it('treats regular SkillView content as loaded', () => {
    const display = getSkillViewDisplay(
      { name: 'browser-use' },
      '---\nname: browser-use\n---\n# Browser workflow'
    )

    expect(display).toMatchObject({
      name: 'browser-use',
      loaded: true,
      message: ''
    })
  })

  it('treats missing name as a failed load', () => {
    const display = getSkillViewDisplay({}, 'Skill name is required.')

    expect(display.loaded).toBe(false)
    expect(display.message).toBe('Skill name is required.')
  })

  it('treats not found as a failed load', () => {
    const display = getSkillViewDisplay({ name: 'missing-skill' }, "Skill 'missing-skill' not found.")

    expect(display.loaded).toBe(false)
    expect(display.message).toBe("Skill 'missing-skill' not found.")
  })

  it('formats collapsed and running labels', () => {
    expect(formatSkillViewLabel({ name: 'browser-use' }, 'en')).toBe('Loaded skill browser-use')
    expect(formatSkillViewRunningLabel({ name: 'browser-use' }, 'en')).toBe('Loading skill browser-use...')
  })
})
