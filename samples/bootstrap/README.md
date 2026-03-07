# DotCraft Bootstrap Samples

**[中文](./README_ZH.md) | English**

Bootstrap file samples that can be copied directly into your workspace `.craft/` directory to quickly customize the agent's role, tone, and user context.

## Usage

Copy the files from any sample role directory into your workspace:

```text
<workspace>/.craft/AGENTS.md
<workspace>/.craft/SOUL.md
<workspace>/.craft/USER.md
```

You can also mix files from different sample directories and tailor them to your own scenario.

## File Reference

| File | Purpose |
|------|------|
| `AGENTS.md` | Agent responsibilities, behavior boundaries, and answer rules |
| `SOUL.md` | Personality, tone, and expression style |
| `USER.md` | User profile, audience background, and communication preferences |

## Sample List

| Directory | Description |
|------|------|
| [qq-group-assistant](./qq-group-assistant) | Chinese QQ group assistant template for Q&A, troubleshooting, and project support |
| [senior-engineer](./senior-engineer) | English senior engineer template for architecture, review, and debugging |

## Notes

These files are plain Markdown with no special format requirements. DotCraft reads them from `.craft/` and injects them into the system prompt.
