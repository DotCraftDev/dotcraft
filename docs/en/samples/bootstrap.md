# DotCraft Bootstrap Samples

Bootstrap file samples that can be copied directly into your workspace `.craft/` directory to quickly customize the agent's role, tone, and user context.

## Usage

Sample files are named with a `.template.md` suffix (e.g. `AGENTS.template.md`). To use a sample:

1. Copy the `.template.md` files from the sample role directory into your workspace `.craft/` folder.
2. Rename each file by removing the `.template` part so they become `AGENTS.md`, `SOUL.md`, and `USER.md`.

```text
# Example: from samples/bootstrap/senior-engineer/
<workspace>/.craft/AGENTS.md   (from AGENTS.template.md)
<workspace>/.craft/SOUL.md     (from SOUL.template.md)
<workspace>/.craft/USER.md     (from USER.template.md)
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
| [qq-group-assistant](https://github.com/DotHarness/dotcraft/tree/master/samples/bootstrap/qq-group-assistant) | Chinese QQ group assistant template for Q&A, troubleshooting, and project support |
| [senior-engineer](https://github.com/DotHarness/dotcraft/tree/master/samples/bootstrap/senior-engineer) | English senior engineer template for architecture, review, and debugging |

## Notes

These files are plain Markdown with no special format requirements. In this repository they are stored as `*.template.md`; DotCraft only loads them after you copy them into your workspace `.craft/` and rename to `.md`. DotCraft then reads them from `.craft/` and injects them into the system prompt.
