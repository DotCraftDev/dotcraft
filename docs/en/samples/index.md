# DotCraft Samples

These samples help you validate DotCraft from different entry points: workspace templates, API calls, AG-UI frontend, automations, hooks, and built-in skills.

## Quick Start

For first-time validation:

1. Start with [Workspace Sample](./workspace.md) to confirm workspace structure and model configuration.
2. Choose API, AG-UI, Automations, or Hooks samples based on your goal.
3. Move the sample config back into your real project's `.craft/`.

## Configuration

Sample source files remain under [samples/](https://github.com/DotHarness/dotcraft/tree/master/samples). The docs explain how to use each sample; inspect the source directory for concrete code, templates, and scripts.

## Usage Examples

| Goal | Sample |
|------|--------|
| Try a complete workspace | [Workspace Sample](./workspace.md) |
| Call the OpenAI-compatible API | [API Samples](./api.md) |
| Run the AG-UI frontend | [AG-UI Client](./ag-ui-client.md) |
| Configure local automations | [Automations Samples](./automations.md) |
| Write lifecycle hooks | [Hooks Samples](./hooks.md) |
| Prepare project bootstrap context | [Bootstrap Samples](./bootstrap.md) |
| Reuse built-in skill templates | [Skills Samples](./skills.md) |

## Advanced Topics

- Config snippets from samples can be moved into a real project's `.craft/config.json`.
- Token-based samples should prefer environment variables; do not commit secrets.
- Test automation samples in a disposable workspace first.

## Troubleshooting

### Sample source files are missing

Confirm you are at the DotCraft repository root. Sample source lives under `samples/`; docs pages live under `docs/samples/`.

### Copied sample config does not apply

Confirm the config is in the current workspace's `.craft/config.json`, then restart the relevant host. Startup-level fields are not hot-reloaded.

### You are not sure which sample to run first

Run Workspace Sample first; it covers workspace layout, config locations, and recommended start order.
