# DotCraft Skills Samples

`samples/skills/` contains skill examples that can be copied into a workspace to guide agents through project-specific development rules, module conventions, and large feature workflows.

To search and install third-party skills in Desktop, see [Search and Install Skills](../skills/marketplace.md). This page only covers copying and adapting the sample skills in this repository.

## What Is Included

| Directory | Use |
|-----------|-----|
| [dev-guide](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/dev-guide) | Project development guide example, including module development references. |
| [feature-workflow](https://github.com/DotHarness/dotcraft/tree/master/samples/skills/feature-workflow) | Large feature workflow example for planning, implementation, and verification. |

## Usage

1. Create `.craft/skills/` in your workspace.
2. Copy the sample directory you need, such as `.craft/skills/dev-guide/`.
3. Adjust `SKILL.md` and supporting files to match your project.
4. Ask the agent to use that skill in a task, or make it part of your project defaults.

## Guidance

- Use `dev-guide` for stable engineering norms.
- Use `feature-workflow` for complex changes across modules.
- Keep the structure when copying, then replace terms, paths, and acceptance criteria with project-specific details.
