# Search and Install Skills

DotCraft Desktop's Skills page can search both locally installed skills and external skill markets. You can inspect the skills already available in the current workspace, then install third-party skills from SkillHub / ClawHub when they fit your project.

![Skills page](https://github.com/DotHarness/resources/raw/master/dotcraft/skills.png)

## Skill Sources

DotCraft shows three local skill sources in the Skills page:

| Source | Description |
|--------|-------------|
| System | Built-in skills shipped with DotCraft. |
| Workspace | Skills under the current workspace's `.craft/skills/` directory. |
| Personal | User-global skills that can be reused across workspaces. |

Marketplace search queries:

- SkillHub
- ClawHub

Marketplace results are not local enabled skills by default. They become local workspace skills only after installation.

## Search in Desktop

1. Open DotCraft Desktop.
2. Go to **Skills**.
3. Type a keyword in the browse-page search box.

The search box does two things:

- Filters locally installed skills.
- Searches SkillHub / ClawHub marketplace results when the query is not empty.

![Skill market search results](https://github.com/DotHarness/resources/raw/master/dotcraft/skill-hub.png)

The source filter can switch between `All / System / Personal / Marketplace`. This only changes the current browse results; it does not enable or disable skills.

## Install from the Marketplace

1. Click a marketplace skill in the search results.
2. Read the README, description, and source link in the detail view.
3. Click install, update, or reinstall.
4. Refresh the local skill list after installation.

Marketplace skills are installed into the current workspace:

```text
.craft/skills/<skill-name>/
```

DotCraft writes an install marker inside the skill directory:

```text
.craft/skills/<skill-name>/.dotcraft-market.json
```

The marker records the provider, version, and update state. If a skill with the same name already exists, Desktop asks for confirmation before replacing or updating it.

## Manage Enabled State

The browse page is for discovery, details, and installation. To enable or disable skills in bulk:

1. Click **Manage** in the upper-right corner of the Skills page.
2. Search installed skills in the manage page.
3. Use the switch on each row to enable or disable a skill.

The manage page does not search SkillHub / ClawHub. It only manages skills that are already installed locally.

## Security and Trust

SkillHub / ClawHub are external sources. Before installing a skill, read its README and source link to make sure it fits your project constraints.

Installing a skill adds new workflow guidance to the workspace. For unfamiliar skills, validate them first in a branch or controlled workspace. If marketplace search fails or the network is unavailable, local skill search and management still work.

## Related Docs

- [Agent Skill Self-Learning](./agent-self-learning.md)
- [Skills Samples](../samples/skills.md)
