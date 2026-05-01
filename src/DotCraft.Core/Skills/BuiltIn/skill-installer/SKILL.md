---
name: skill-installer
description: "Use when installing a DotCraft skill from a local folder, archive, GitHub checkout, or market-provided download and then checking whether it needs workspace-specific adaptation."
tools: Exec, SkillView
---

# Skill Installer

Use this workflow when the user wants to install, import, test, or adapt a DotCraft skill. The installation surface is ordinary shell and file work plus the DotCraft skill verifier; there is no dedicated install tool.

## Workflow

1. Prepare a candidate directory.
   - If the user provides a local directory, inspect it directly.
   - If the user provides an archive or URL, download/extract it into a temporary directory in the workspace.
   - If the skill is inside a repository, clone or copy the repository, then point at the subdirectory containing `SKILL.md`.
2. Ensure the candidate directory is a DotCraft skill bundle.
   - `SKILL.md` must be at the candidate root.
   - The frontmatter `name` must be the intended skill name.
   - Supporting files should stay under `scripts/`, `assets/`, or `agents/openai.yaml`.
3. Run verification:

```powershell
dotcraft skill verify --candidate "<candidate-dir>" --json
```

4. If verification fails, fix the candidate directory and run verification again.
5. Install after verification succeeds:

```powershell
dotcraft skill install --candidate "<candidate-dir>" --source "<where this came from>" --json
```

Use `--overwrite` only when the user explicitly wants to replace the current workspace source skill.

## Post-install Review

After installation, load the installed instructions with `SkillView(name)`. Then review the skill against the current workspace and environment by doing only checks that the skill itself makes relevant.

Examples of useful checks:

- If it assumes `python`, check the actual Python command/version or virtual environment.
- If it assumes a CLI tool, check whether that tool exists and whether the expected command shape works.
- If it references scripts or assets, confirm those files exist in the installed skill.
- If it assumes paths, package managers, shells, or config files, check those in the workspace.

Only write a workspace adaptation when you find a concrete mismatch. If `SkillManage` is available, use it to patch the effective skill so future runs remember the workspace-specific fix. If `SkillManage` is unavailable, report the mismatch and the suggested adaptation instead of editing the source skill.

Do not invent compatibility rules that the skill does not imply. Do not rewrite a freshly installed skill just to summarize the installation.
