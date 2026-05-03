---
name: external-process-echo
description: Use this skill when the user wants to verify or demonstrate the External Process Echo plugin dynamic tool.
---

# External Process Echo

Use the `EchoText` tool to echo user-provided text through the plugin-owned local process.

## Workflow

1. Ask for the text to echo if the user did not provide it.
2. Call `EchoText` with the `text` argument.
3. Report the echoed text and, when useful, the structured length result.

## Verification

- Confirm the tool response includes a text content item.
- Confirm `structuredResult.echo` matches the input text.
