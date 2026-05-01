# SkVM-Style Skill Variants Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-05-01 |
| **Parent Specs** | [Skill 2.0](skill-2.0.md), [Session Core](session-core.md), [AppServer Protocol](appserver-protocol.md) |

Purpose: Define DotCraft's SkVM-style skill variant layer. This spec focuses on source and variant isolation, safe agent self-learning, run-evidence-backed skill maintenance, skill installation, effective skill viewing, and reversible skill adaptation. It intentionally does not define GraSP-style multi-skill planning.

## 1. Scope

This spec refines the SkVM-style portion of [Skill 2.0](skill-2.0.md). It treats a `SKILL.md` bundle as source material and lets DotCraft generate, select, and maintain environment-specific variants without damaging the source skill.

In scope:

- Preserve source skills as stable, user-visible, reviewable artifacts.
- Store generated skill variants as full skill directory snapshots.
- Route agent self-learning edits to variants instead of source skills.
- Record skill execution evidence from thread, turn, trace, approval, tool, model, environment, and user feedback data.
- Select the best compatible variant for the current model, harness, tool profile, OS, workspace, and source fingerprint.
- Provide one user-facing recovery action: restore the original source skill.
- Provide an effective skill view API/tool so agents and clients read the resolved source-or-variant skill instead of treating `ReadFile` as the skill abstraction.
- Define a skill installation flow that can compile imported skills into variants at install time.
- Define future AppServer surfaces without requiring an immediate protocol break.

Out of scope:

- GraSP-style graph planning and verified graph execution.
- Replacing the normal DotCraft ReAct/tool loop.
- User-facing variant version control, manual accept/reject flows, or source-promotion workflows.
- Automatically applying variant edits back into source skills.
- Full SkVM primitive benchmarking as a requirement for ordinary use.
- Executing arbitrary generated code outside DotCraft's existing tool and approval policy.

## 2. Research Mapping

The umbrella [Skill 2.0](skill-2.0.md) spec records the research basis. This spec uses only the SkVM side of that design.

Relevant SkVM concepts and DotCraft interpretation:

| SkVM Concept | DotCraft Interpretation |
|--------------|-------------------------|
| Skills as code | A skill bundle is a source artifact with identity, content hash, provenance, and generated derivatives. |
| Heterogeneous processors | A DotCraft target is the tuple of model, harness, tools, OS, sandbox posture, approval policy, and workspace conventions. |
| Capability profile | DotCraft starts with a lightweight target signature derived from known runtime facts and observed failures. Explicit primitive profiling is optional future work. |
| AOT compilation | DotCraft can generate a variant before use by binding environment details, replacing unsupported assumptions, and clarifying tool usage. |
| JIT optimization | DotCraft can improve a variant after real runs using evidence, repairs, user corrections, and verifier outcomes. |
| Optimizer artifacts | DotCraft may use temporary working copies while improving a skill. The user-facing product concept is still just a skill plus "restore original skill". |

Sources:

- [SkVM arXiv 2604.03088](https://arxiv.org/abs/2604.03088)
- [SkVM repository architecture notes](https://github.com/SJTU-IPADS/SkVM/blob/main/docs/architecture.md)

## 3. Current DotCraft Baseline

Current self-learning is prompt-driven:

- `PromptBuilder` injects "Skill Self-Learning" guidance when the `SkillManage` tool is available.
- `SkillManageTool` exposes `create`, `edit`, `patch`, `write_file`, `remove_file`, and `delete`.
- `WorkspaceFileSkillMutationApplier` writes directly to `.craft/skills/{name}/`.
- `create` and `delete` are approval-gated when an approval service is available.
- `edit`, `patch`, `write_file`, and `remove_file` currently do not require approval.
- `SkillsLoader` resolves source skills from the DotCraft skill roots and injects selected `SKILL.md` content into the agent context.
- AppServer currently exposes `skills/list`, `skills/read`, and `skills/setEnabled`, but not effective skill views, variants, installers, or run records.

This baseline is useful, but it makes source skills the mutable self-learning target. The variant layer changes that default.

## 4. Design Goals

1. Source skills are not the agent's scratchpad.
2. Users should not need to learn that DotCraft has a VM-style skill layer.
3. Agent self-learning can become more autonomous because the default write target is recoverable.
4. Variants can accumulate long-term procedural memory without making source skill review noisy.
5. Existing `SKILL.md` files and existing skill discovery remain valid.
6. Skill reading must go through an effective skill view when variant mode is enabled.
7. Skill installation should preserve manual filesystem workflows while adding an agent-assisted path that compiles imported skills.
8. The first implementation can be internal and prompt-facing before the AppServer protocol grows a stable public surface.

## 5. Terminology

| Term | Definition |
|------|------------|
| **Source Skill** | The canonical installed skill bundle. Existing `SKILL.md` compatibility belongs here. Source skills may be built-in, workspace, user-global, imported, or marketplace-provided. |
| **Source Fingerprint** | A stable hash of the source skill bundle content and selected metadata. It binds a variant to the exact source it was derived from. |
| **Target Signature** | The runtime tuple a variant is optimized for: DotCraft harness version, model id, available tools, OS, shell, sandbox posture, approval policy, workspace path identity, and relevant config flags. |
| **Skill Variant** | A generated full skill directory snapshot derived from one source skill and one target signature. It can include `SKILL.md`, `scripts/`, `assets/`, and manifest metadata. |
| **Effective Skill** | The skill bundle DotCraft actually presents to the agent after resolving source, compatible variant, staleness, and restore state. It is the only skill body agents should normally read. |
| **Current Variant** | The variant currently selected by the resolver for a source skill and target signature. There is at most one current variant per source skill per target signature. |
| **Optimizer Working Copy** | A temporary internal artifact used to generate or update a variant. It is not a user-facing object. |
| **Skill Run Record** | Durable evidence about a skill use attempt, including input summary, selected source or variant, model, tools, environment, outcome signal, user correction, and repair notes. |
| **Restore Original Skill** | The single user-facing recovery action. It removes or disables the current variant for the target so the resolver falls back to the source skill. |
| **Skill Activity Metadata** | Aggregated metadata derived from run records, such as use count, last used time, success/failure counts, last repair note, and last restore time. |

## 6. Non-Negotiable Invariants

- Source skills MUST remain valid `SKILL.md` bundles.
- Source skills MUST NOT be modified by agent self-learning.
- Variants MUST be stored outside the source skill directory.
- A variant MUST record the source skill identity and source fingerprint it was derived from.
- A variant MUST be considered stale when its source fingerprint no longer matches the current source.
- A stale variant MUST NOT be auto-selected unless a future compatibility policy explicitly allows it.
- Restore original skill MUST be possible without reading or applying a diff.
- Variant support MUST degrade to existing source skill behavior when disabled.
- Generated supporting files MUST obey the same path restrictions as source skill supporting files unless a stricter variant-specific policy is configured.
- AppServer clients MUST NOT be required to understand variants for ordinary skill usage until a new capability flag is advertised.
- Agents MUST use a skill-aware view/read method for skill bodies when variant mode is enabled. Raw file reads are still allowed for diagnostics, but are not the authoritative skill abstraction.

## 7. Storage Model

DotCraft should keep source skills and generated variants in separate roots under the DotCraft workspace directory.

Recommended layout:

```text
.craft/
  skills/
    {source-skill}/
      SKILL.md
      scripts/
      assets/
      .builtin
  skill-variants/
    index.json
    {source-key}/
      {variant-id}/
        manifest.json
        skill/
          SKILL.md
          scripts/
          assets/
  skill-runs/
    {yyyy}/
      {mm}/
        {run-id}.json
  skill-activity.json
```

Notes:

- `.craft/skills/` remains the source skill root used by existing loading behavior.
- `.craft/skill-variants/` is never scanned as a normal skill source.
- Each variant stores a full usable skill bundle under `skill/`.
- `source-key` should be filesystem-safe and include source kind plus skill name, for example `workspace.my-skill` or `user.code-review`.
- `index.json` is an acceleration cache. The per-variant `manifest.json` is authoritative.
- Internal optimizer working copies MAY be stored as temporary directories, but they are not part of the stable user-facing storage contract.
- `skill-activity.json` stores compact aggregate metadata so UI and resolver decisions do not need to scan every run record.
- User-global variant storage MAY be added later, but the initial design should prefer workspace-scoped variants because target signatures often include workspace-specific conventions.

## 8. Manifest Shapes

### 8.1 Variant Manifest

```json
{
  "schemaVersion": 1,
  "variantId": "var_20260501_abcdef",
  "source": {
    "name": "browser-use",
    "sourceKind": "builtin",
    "path": "F:/repo/.craft/skills/browser-use/SKILL.md",
    "fingerprint": "sha256:...",
    "frontmatter": {
      "name": "browser-use",
      "version": "1.0.0"
    }
  },
  "target": {
    "harness": "dotcraft",
    "harnessVersion": "0.0.0",
    "model": "openai/gpt-5.2",
    "os": "windows",
    "shell": "powershell",
    "sandbox": "host",
    "toolProfileHash": "sha256:...",
    "approvalPolicy": "default"
  },
  "status": "current",
  "createdAt": "2026-05-01T00:00:00Z",
  "updatedAt": "2026-05-01T00:00:00Z",
  "parentVariantId": null,
  "provenance": {
    "kind": "selfLearning",
    "threadId": "thread_...",
    "turnId": "turn_...",
    "runRecordIds": ["run_..."],
    "optimizerModel": "openai/gpt-5.2"
  },
  "quality": {
    "confidence": 0.72,
    "successCount": 3,
    "failureCount": 1,
    "lastSelectedAt": "2026-05-01T00:00:00Z",
    "lastOutcome": "success"
  },
  "summary": "Adapted browser-use for PowerShell path handling and local desktop browser testing."
}
```

Required fields:

- `schemaVersion`
- `variantId`
- `source.name`
- `source.sourceKind`
- `source.fingerprint`
- `target.harness`
- `target.model`
- `target.toolProfileHash`
- `status`
- `createdAt`
- `updatedAt`
- `provenance.kind`

Allowed `status` values:

| Status | Meaning |
|--------|---------|
| `current` | Selected by policy for the source and target signature. |
| `stale` | Source fingerprint no longer matches. |
| `restored` | User restored the original source skill; this variant is no longer selected. |
| `superseded` | Replaced by a newer current variant. |

### 8.2 Activity Metadata

```json
{
  "schemaVersion": 1,
  "skills": {
    "builtin.browser-use": {
      "useCount": 42,
      "variantUseCount": 31,
      "successCount": 28,
      "failureCount": 4,
      "lastUsedAt": "2026-05-01T00:00:00Z",
      "lastVariantUpdatedAt": "2026-05-01T00:00:00Z",
      "lastRestoredAt": null,
      "recentRunRecordIds": ["run_..."]
    }
  }
}
```

Activity metadata is derived data. If it is missing or corrupt, DotCraft may rebuild it from run records.

## 9. Fingerprints and Target Signatures

### 9.1 Source Fingerprint

The source fingerprint SHOULD hash:

- `SKILL.md` bytes.
- Supporting files under allowed skill bundle directories.
- File relative paths.
- Optional frontmatter metadata that changes behavior.

The source fingerprint SHOULD NOT hash volatile UI metadata such as icon cache data URLs.

### 9.2 Target Signature

The target signature SHOULD include:

- DotCraft harness id and version.
- Main agent model id.
- Available agent tool names and tool profile names.
- OS and shell.
- Sandbox mode.
- Approval policy relevant to file and shell actions.
- Workspace identity hash, not the raw path, unless raw path is already exposed elsewhere.
- Skill self-learning configuration values that affect mutation limits.

The resolver MAY use a relaxed target match for low-risk fields. For example, a variant generated for `gpt-5.2` can be considered compatible with `gpt-5.2-mini` only if policy explicitly allows model-family fallback.

## 10. Resolver Behavior

The variant-aware resolver expands current skill resolution:

1. Resolve the source skill exactly as today.
2. Compute the source fingerprint.
3. Compute the target signature.
4. Find compatible variants for `(source, sourceFingerprint, targetSignature)`.
5. Exclude variants with `restored`, `stale`, or incompatible status.
6. Pick the current variant if one exists.
7. If no current variant exists, optionally create or refresh one when auto-compile is enabled.
8. Fall back to the source skill.

Prompt injection behavior:

- The skill summary should continue to show the source skill name.
- If a variant is selected, the location shown to the agent SHOULD be the variant's `SKILL.md` path.
- Full context loading SHOULD load the variant body, not the source body, when a variant is selected.
- The agent SHOULD normally see the effective skill, not the VM mechanics. Debug metadata may say whether the effective body came from source or variant.

Staleness:

- On source fingerprint mismatch, the variant becomes `stale`.
- A stale variant may be used as evidence for generating a new variant, but it is not directly injected.
- The source skill is the immediate fallback.

## 11. Effective Skill View

Variant mode means a raw file path is no longer the complete skill abstraction. `ReadFile` can still read a physical source or variant file, but agents and clients need a skill-aware view method that resolves the effective skill first.

`SkillView` is intentionally small. It is a loader, not a diagnostics API. The program decides whether source or variant content should be used; the agent receives only the skill instructions it should follow.

Required behavior:

- The view method accepts a skill name.
- It resolves the source skill and current compatible variant using Section 10.
- It returns the effective `SKILL.md` body as plain text.
- It strips YAML frontmatter by default, matching prompt-injected skill content.
- It does not return variant id, source path, effective path, fingerprint, origin, or supporting-file listings.
- It does not proactively include supporting files. If the skill body references `scripts/` or `assets/`, the agent can read those files through normal file tools using paths or instructions from the skill content.
- It must not expose variant terminology to the agent.

Candidate agent-facing tool:

```text
SkillView(name)
```

Candidate result:

```text
# Browser Use

Use this skill when...
```

The tool result MAY be wrapped in the host's normal tool envelope, but the semantic payload is only the effective skill body.

Prompt behavior:

- The skills summary should instruct the agent to use `SkillView` for loading skills when the tool is available.
- A fallback to `ReadFile` is acceptable only when `SkillView` is unavailable or when debugging a specific physical file.
- AppServer `skills/read` remains source-oriented for compatibility, while a new `skills/view` method represents the effective skill and returns only the body content by default.
- Diagnostics that need paths, fingerprints, variant ids, or run records should use separate developer-oriented methods, not `SkillView`.

## 12. SkillManage v2 Semantics

`SkillManage` remains the agent-facing entry point, but variant mode changes the default write target.

### 12.1 Default Policy

When variant mode is enabled:

- `create` creates a new source workspace skill and remains approval-gated.
- `delete` deletes a source workspace skill only with approval, as today.
- `edit`, `patch`, `write_file`, and `remove_file` operate on the current variant for the named source skill.
- If no current mutable variant exists, the first mutation forks the current source skill into a new variant, then applies the mutation there.
- Built-in, user-global, imported, and marketplace source skills are all treated as read-only from the agent's perspective.
- Workspace source skills are also treated as read-only by default for self-learning mutations.

This means the agent can keep improving a skill without silently changing the user's source artifact.

### 12.2 Return Shape

The existing `SkillMutationResult` can be extended compatibly:

```json
{
  "success": true,
  "message": "Patched skill 'browser-use'. The original skill was not modified.",
  "path": "F:/repo/.craft/skill-variants/builtin.browser-use/var_.../skill/SKILL.md",
  "replacementCount": 1
}
```

The agent-facing result SHOULD avoid variant ids, source fingerprints, and source paths. Developer diagnostics may expose those through separate APIs.

### 12.3 Restore Original Skill

Restore original skill is the only user-facing variant management action.

Restore behavior:

- Mark the current variant as `restored`.
- Preserve a lightweight tombstone so the resolver does not immediately reselect the same variant without new evidence.
- Large variant snapshots may be garbage-collected later, but the restored state must remain visible to the resolver.
- Record `lastRestoredAt` in skill activity metadata.
- Refresh skill descriptors after restore.
- The next resolver pass must fall back to the source skill.
- Future self-learning may create a new variant only after new evidence appears.

Source promotion is intentionally absent from the core UX. If a future advanced maintenance command copies variant content back to source, it should be treated as a source edit/export feature, not as part of normal self-learning.

## 13. Skill Installation and Import

Manual installation remains valid: users may still place a skill folder under `.craft/skills/{name}/` or the user skill root. DotCraft should detect it as a source skill exactly as today.

DotCraft should also provide a built-in skill for agent-assisted installation, similar in spirit to Codex's `skill-installer` skill:

- List installable skills from configured catalogs.
- Install a named curated skill.
- Install from a GitHub repository path or URL.
- Install from a local path when the user supplies one.
- Refuse to overwrite an existing source skill unless the user explicitly asks.
- After copying the source skill, validate frontmatter and supporting-file constraints.
- Compute the source fingerprint.
- Run the initial compile/adaptation flow for the current target signature.
- Save the result as the current variant when compilation succeeds, or fall back to source when it does not.
- Tell the user only that the skill was installed and is ready, plus any restart/session-refresh requirement.

Recommended built-in skill name:

```text
skill-installer
```

The installer is an agent workflow skill, not the only install path. It exists so imported or downloaded skills naturally pass through validation and initial variant compilation without making the user manage that process.

## 14. Run Records

Skill Run Records are the evidence layer that makes variants long-lived memory instead of one-off rewrites. They are workspace-level skill memory with references back to sessions, not session-owned data.

Recommended schema:

```json
{
  "schemaVersion": 1,
  "runId": "run_20260501_abcdef",
  "threadId": "thread_...",
  "turnId": "turn_...",
  "createdAt": "2026-05-01T00:00:00Z",
  "task": {
    "summary": "Fix a failing PowerShell build script.",
    "inputHash": "sha256:..."
  },
  "skill": {
    "name": "powershell-build",
    "sourceKind": "workspace",
    "sourceFingerprint": "sha256:...",
    "variantId": "var_...",
    "origin": "variant"
  },
  "target": {
    "harness": "dotcraft",
    "model": "openai/gpt-5.2",
    "toolProfileHash": "sha256:...",
    "os": "windows",
    "shell": "powershell"
  },
  "signals": {
    "outcome": "success",
    "confidence": 0.75,
    "userCorrection": false,
    "testsPassed": true,
    "approvalDeclined": false
  },
  "cost": {
    "toolCalls": 7,
    "durationMs": 123000,
    "inputTokens": 12000,
    "outputTokens": 3000
  },
  "notes": {
    "failureMode": null,
    "repair": "Use PowerShell-native path quoting.",
    "verifier": "dotnet test"
  }
}
```

Outcome values:

- `success`
- `failure`
- `partial`
- `unknown`

Run records SHOULD be compact. Large trace payloads should stay in Trace storage and be referenced by id.

Lifecycle:

- A pending record may be opened when a skill is loaded through the effective skill view or injected into context.
- The record is finalized at turn completion when DotCraft can attach outcome signals from tool results, tests, approvals, user corrections, or agent self-report.
- Records keep `threadId` and `turnId` references for traceability, but they remain under `.craft/skill-runs/` so they survive session archival.
- If a thread is deleted, run records may keep compact summaries and hashes while dropping links to unavailable traces.
- Activity metadata is updated from finalized records.
- Pruning should keep recent records and aggregate counters. A reasonable initial policy is max records per skill plus age-based cleanup for unknown/low-signal outcomes.

Metadata needed for management and selection:

- Use count per source skill.
- Use count per current variant.
- Last used time.
- Success, failure, partial, and unknown counts.
- Last user correction time.
- Last variant update time.
- Last restore time.
- Recent run record ids.

## 15. Variant Generation

DotCraft may generate variants through several paths:

| Path | Trigger | Output |
|------|---------|--------|
| Lazy fork | Agent calls `SkillManage` on a source skill without a current variant. | Current variant with one mutation applied. |
| AOT adaptation | Resolver or installer sees a source skill likely incompatible with the current target. | Current variant or source fallback. |
| JIT optimization | Post-run evidence indicates stale instructions, recurring failure, or user correction. | Updated current variant. |
| Manual customization | User asks to customize a skill for this workspace/model. | Current variant. |
| Import hardening | User imports a downloaded skill through the installer. | Source skill plus initial current variant when compilation succeeds. |

Generation requirements:

- The generator MUST copy the full source skill bundle before editing.
- The generator MUST validate `SKILL.md` frontmatter and size limits.
- The generator MUST validate supporting file paths.
- The generator SHOULD record root cause and confidence.
- The generator SHOULD prefer narrow changes tied to evidence.
- The generator MUST preserve the source skill name in `SKILL.md` unless a deliberate fork creates a new source skill.
- Failed generation MUST fall back to the source skill without requiring user intervention.

## 16. User Experience

Default chat experience:

- Agent uses source or variant skills naturally.
- Self-learning edits no longer require routine approval because they target variants.
- When a skill improves itself, DotCraft may summarize this as "I updated the workspace adaptation for this skill" only when useful. Casual users do not need to hear about variants.
- Installation says the skill is installed and ready; it does not describe compilation unless diagnostics are requested.

Skills UI should keep the core model simple:

- Skill name, description, source, enabled state, and readiness.
- Optional "adapted for this workspace" badge.
- Last used time and lightweight use count if helpful.
- One recovery action: restore original skill.
- A diagnostics panel may show effective path, source path, variant id, source fingerprint, recent runs, and staleness for developers.

Restore UX:

- "Restore original skill" removes the current adaptation.
- No diff application is needed.
- The next prompt build uses the source skill.

The UI should expose only restore original skill for variant recovery. Advanced diagnostics may expose raw paths and ids, but should not introduce a normal variant review workflow.

## 17. Security and Trust

Variant isolation improves safety but does not remove normal tool risk.

Required protections:

- Variants inherit all source skill file path constraints.
- Variant-generated scripts are just skill supporting files until invoked through normal tools.
- Existing shell, file, approval, sandbox, and path blacklist policies remain authoritative.
- Downloaded or marketplace skills SHOULD be tagged with provenance, and their variants SHOULD keep that provenance.
- Variant manifests SHOULD avoid storing secrets, full raw prompts, or unnecessary file contents.
- Run records SHOULD store hashes and summaries by default, linking to trace ids for detailed inspection.
- Installer workflows MUST validate downloaded or copied skills before they become source skills.

## 18. Configuration

Recommended future config:

```json
{
  "Skills": {
    "SelfLearning": {
      "Enabled": true,
      "VariantMode": "enabled",
      "AutoCompileOnInstall": true,
      "AutoCreateVariants": true,
      "MaxVariantsPerSkill": 20,
      "MaxRunRecordsPerSkill": 500
    }
  }
}
```

Allowed `VariantMode` values:

- `disabled`: current source-only behavior.
- `passive`: record run evidence but do not create variants automatically.
- `enabled`: create and inject current variants by policy.

Initial implementation MAY hard-code conservative defaults before exposing all settings.

## 19. AppServer Extension Surface

No immediate protocol break is required. Future public surfaces should be guarded by a new capability flag, for example `capabilities.skillVariants`.

Candidate methods:

| Method | Purpose |
|--------|---------|
| `skills/view` | Read the effective skill content after source/variant resolution. |
| `skills/restoreOriginal` | Restore the source skill by removing or disabling the current adaptation. |
| `skills/install` | Install a skill from a catalog, GitHub URL/path, or local path and optionally compile it. |
| `skills/runs/list` | List compact run records for a skill, mainly for diagnostics. |

The existing `skills/list` may later include optional fields:

- `effectiveOrigin`
- `adapted`
- `lastUsedAt`
- `useCount`
- `sourceFingerprint`

Those fields must be optional and capability-gated.

## 20. Resolved Design Decisions

These decisions define the first implementation target:

1. Target signatures include workspace identity by default. Variants are workspace adaptations, not portable artifacts. Future export/import may intentionally carry selected learning across workspaces, but the default resolver must treat workspace identity as part of compatibility.
2. All variants start workspace-local. User-global source skills may receive workspace-local adaptations, but DotCraft should not create user-global variants in the initial design.
3. Run evidence is compact, bounded, and privacy-conscious. The default retention target is `MaxRunRecordsPerSkill = 500`; low-signal `unknown` records may be pruned by age, while aggregate activity metadata may be retained and rebuilt. Run records store summaries, hashes, counters, and trace ids rather than raw full prompts or secrets.
4. `skills/read` remains source-oriented for compatibility. `skills/view` is the effective skill reader. Clients and agents should migrate to `skills/view` / `SkillView` when they need the instructions DotCraft will actually use.
5. Restore original skill marks the current variant as `restored` and preserves a lightweight diagnostic tombstone. DotCraft may later garbage-collect large variant snapshots, but the resolver must keep enough state to avoid immediately reselecting the restored variant without new evidence.

## 21. Implementation Notes

This spec does not define milestones. Sequencing should be decided after the design is finalized and the current skill, trace, session, AppServer, and UI infrastructure are re-evaluated.

Likely implementation areas:

- `SkillsLoader`: variant-aware resolution and context loading.
- `ISkillMutationApplier`: variant-aware mutation routing.
- `SkillManageTool`: response shape and behavior text updates.
- `PromptBuilder`: compact variant self-learning guidance.
- `TraceStore` and `SessionPersistenceService`: run evidence extraction.
- New effective skill view service/tool and optional AppServer `skills/view`.
- Built-in `skill-installer` workflow and install service.
- AppServer skills section: optional view, restore, install, and run diagnostics methods after the internal model stabilizes.
- Skills UI: simple readiness/adaptation state, usage metadata, and restore original action.
