---
max_rounds: 10
---

You are running a **local automation** workflow.

## Task

- **ID**: {{ task.id }}
- **Title**: {{ task.title }}

## Instructions

{{ task.description }}

## Workspace

The agent working directory is:

`{{ task.workspace_path }}`

Use file and shell tools as needed. When finished, summarize what you did for review.
