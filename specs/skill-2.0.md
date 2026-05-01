# DotCraft Skill 2.0 Design Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-27 |
| **Parent Specs** | [Session Core](session-core.md), [AppServer Protocol](appserver-protocol.md) |
| **Child Specs** | [SkVM-Style Skill Variants](skvm-style-skill-variants.md) |

Purpose: Define **Skill 2.0**, an experimental extension of DotCraft's self-learning skill system. Skill 2.0 makes skills safer to evolve, easier to adapt to the current runtime environment, and more useful in complex tasks through paper-backed skill planning and skill-variant management.

## 1. Scope

Skill 2.0 extends the current `SKILL.md` model without breaking existing skills.

In scope:

- Derive reusable experience from existing Thread and Trace persistence.
- Store skill run records for successful, failed, and repaired skill usage.
- Add safe skill variants and proposals instead of mutating source skills directly.
- Add an optional GraSP-style planner that recommends skill composition plans for complex tasks.
- Keep normal DotCraft usage lightweight: most benefits should appear as better agent behavior, not new required user setup.

Out of scope for the initial design:

- Replacing the normal ReAct/tool loop.
- Requiring all skills to declare formal schemas before they can run.
- Automatically rewriting trusted source skills without review.
- Exposing research internals to casual users.

## 2. Research Basis

Skill 2.0 uses two complementary research directions.

| Paper | DotCraft Interpretation |
|-------|-------------------------|
| [GraSP: Graph-Structured Skill Compositions for LLM Agents](https://arxiv.org/abs/2604.17870), Tianle Xia, Lingxiang Hu, Yiding Sun, Ming Xu, Lan Xu, Siying Wang, Wei Xu, Jie Jiang, arXiv:2604.17870, 2026. | Skill libraries create an orchestration problem. DotCraft adopts GraSP as the basis for selecting, ordering, verifying, and locally repairing skill usage in complex tasks. |
| [SkVM: Revisiting Language VM for Skills across Heterogenous LLMs and Harnesses](https://arxiv.org/abs/2604.03088), Le Chen, Erhu Feng, Yubin Xia, Haibo Chen, arXiv:2604.03088, 2026. | Skills should be treated like portable code executed by heterogeneous model/harness pairs. DotCraft adopts SkVM as the basis for environment-specific skill variants and safe adaptive optimization. |

GraSP is an execution-planning reference: it introduces memory-conditioned skill retrieval, DAG compilation, node-level verification, local repair, and confidence-based fallback. DotCraft should initially use these ideas as an advisory planning layer, not as a mandatory replacement for the existing agent loop.

SkVM is a storage and adaptation reference: it treats skills as code and model/harness pairs as heterogeneous processors, then uses capability-based compilation, environment binding, concurrency extraction, JIT code solidification, and adaptive recompilation. DotCraft should initially use these ideas to produce reviewable variants and proposals without damaging source skills.

## 3. Core Concepts

| Concept | Definition |
|---------|------------|
| **Source Skill** | The canonical `SKILL.md` under workspace, user, or built-in skill roots. Source skills remain compatible with today's loader. |
| **Skill Variant** | A generated, environment-specific derivative of a source skill. Variants may adapt wording, tool assumptions, verification steps, OS notes, model behavior, or known failure fixes. |
| **Skill Proposal** | A pending change suggested by DotCraft from observed experience. Proposals are reviewable and do not silently replace source skills. |
| **Skill Run Record** | A durable record derived from Thread and Trace data: task summary, selected skills, model, tools, environment, success/failure signal, verifier result, user correction, token/time cost, and repair notes. |
| **Procedure Plan** | A GraSP-inspired task plan containing selected skill nodes, ordering, dependencies, verification points, and fallback behavior. In early designs this is advisory, not a mandatory executor. |

## 4. User Experience

Skill 2.0 should feel like DotCraft gradually becomes better at repeated work while keeping the user in control of durable changes.

Silent improvements:

- DotCraft selects fewer irrelevant skills.
- Repeated workflows become more reliable.
- Environment-specific pitfalls are remembered without requiring users to edit `SKILL.md`.
- Failed skill usage can improve future behavior through proposals or variants.
- Existing source skills remain protected from accidental degradation.

Visible user-facing surfaces:

- In chat, DotCraft may say it found a reusable workflow and ask whether to save or improve a skill.
- In Skills UI, users can inspect source skills, generated variants, and pending proposals.
- In advanced task runs, DotCraft may show a compact "Skill Plan" explaining which skills it intends to combine.
- In automation scenarios, DotCraft can present Skill 2.0 as a reliability feature: repeated jobs improve from prior runs.

Not exposed by default:

- Graph internals, edge types, confidence formulas, and compiler passes.
- Raw Thread/Trace mining details.
- Variant selection knobs unless the user opens advanced settings.

Product highlights:

- "Skills that learn safely from real usage."
- "Paper-backed skill orchestration for complex agent workflows."
- "Workspace-specific skill variants without damaging source skills."
- "Agent experience memory built on DotCraft's existing Thread and Trace history."

## 5. Behavior Model

Skill 2.0 has two cooperating layers.

### 5.1 SkVM-Style Variant Layer

The variant layer optimizes individual skills for the current model, tool profile, operating system, workspace, and observed failure patterns.

Required behavior:

- Read source skills and historical Skill Run Records.
- Produce variants or proposals for the current runtime environment.
- Never mutate source skills without explicit approval or review.
- Prefer fallback to the original source skill when variant confidence is low.
- Preserve source-skill compatibility with existing `SkillsLoader` behavior.

### 5.2 GraSP-Style Planning Layer

The planning layer decides whether a task should use no skill, one skill, multiple flat skills, or a structured procedure plan.

Required behavior:

- Run only when task complexity or retrieval confidence justifies it.
- Retrieve candidate skills from semantic task match plus successful historical run records.
- Produce one of four routes: `NoSkill`, `SingleSkill`, `FlatMultiSkill`, or `ProcedurePlan`.
- Inject compact advisory plans into agent context for complex tasks.
- Preserve mandatory fallback to the existing DotCraft agent loop.

Verified graph execution is reserved for a later automation-focused design. It should not be required for normal interactive tasks until reliability is proven.

## 6. Detailed Design Split

This umbrella spec records the Skill 2.0 direction and shared vocabulary. Detailed designs live in focused child specs:

- [SkVM-Style Skill Variants](skvm-style-skill-variants.md): source and variant isolation, proposal storage, run records, resolver behavior, and variant-aware self-learning.

Implementation sequencing is intentionally omitted from this umbrella spec. Concrete slices should be planned only after the relevant child specs are accepted and the current skill, session, trace, AppServer, and UI infrastructure are re-evaluated.

## 7. Compatibility and Protocol Notes

- Existing `SKILL.md` files remain valid.
- Existing `SkillManage` remains the mutation entry point.
- Skill 2.0 should prefer proposal or variant creation over direct source mutation.
- No AppServer wire break is required initially.
- Future protocol extensions may add `skills/variants/list`, `skills/proposals/list`, `skills/proposals/apply`, and `skills/runs/list`.
- When self-learning is disabled, no skill mutation, proposal application, or variant promotion is exposed to the agent.

## 8. Assumptions

- Skill 2.0 is experimental and should ship behind conservative defaults.
- Variant/proposal safety is more important than immediate automation.
- GraSP and SkVM are complementary: SkVM improves individual skill reliability; GraSP improves multi-skill orchestration.
- DotCraft should prioritize user trust: source skills are stable, learned changes are explainable, and fallback behavior is always available.
