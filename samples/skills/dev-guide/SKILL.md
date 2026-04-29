---
name: dotcraft-dev-guide
description: Development workflow and project-specific norms for DotCraft. Use when changing protocol behavior, or shipping user-facing features. Covers spec-first workflow, testing rules and bilingual docs.
---

# DotCraft Development Guide

Project-specific workflow and norms for DotCraft.
For generic C#/Rust/React style, follow each ecosystem's standard conventions.
For repo orientation, read `CLAUDE.md`.

## Development Workflow

### Spec-First

When modifying protocol designs or process flows defined in `specs/`, update the spec first, then implement.
If a proposed change conflicts with an existing spec, resolve the spec-level conflict before touching code.

### Steps

1. **Plan**: Search the codebase for similar features before adding new abstractions.
2. **Implement**: Follow the project-specific norms below. Add XML docs for public C# APIs. Update user-facing docs in both languages when behavior changes.
3. **Test**: Follow the testing rules below.
4. **Verify**: Confirm changes conform to the relevant spec and docs/examples are in place.

### Testing Rules

Tests are required for:

- Protocol-dependent code (JSON-RPC, wire format, session/appserver protocol): add conformance tests aligned with the spec.
- Complex multi-step flows or state machines: cover key paths and edge cases.

Tests are not required for:

- Small bug fixes that do not change an observable contract.
- Pure UI polish (layout, styling, copy tweaks) in desktop or TUI.
- Trivial refactors with no behavior change.

A test is meaningful only when (applies to C# xUnit and TypeScript tests equally):

- It catches a real regression, not language semantics, trivial getters/setters, or framework internals.
- It is not redundant with existing coverage; check before adding.
- It does not merely restate implementation details via excessive mocking; prefer state/output assertions unless interaction itself is the contract.
- It is not written just to inflate coverage numbers.

Pre-commit: run the relevant full suites for touched areas (`dotnet test` for C#, and corresponding `npm test`/`cargo test` where applicable). Do not commit with known failures.

### Language Preference

- **Code comments**: English
- **User-facing messages**: Use `LanguageService` for bilingual support, e.g. `lang.GetString("中文", "English")`. For CLI strings, the codebase centralizes entries in `DotCraft.Core.Localization.Strings`; search for `Strings.` and `LanguageService` for examples.

## Documentation Guidelines

DotCraft documentation lives under `docs/` as a VitePress documentation site.

When creating or updating documentation:

- Provide all documentation in both Chinese and English.
- Consider whether the documentation location fits the existing site structure. When inserting new documentation, ask the user where it should go unless the user has already approved a location.
- Keep documentation concise and current. Do not include historical explanations, such as old-version migration rationale or why legacy behavior existed.
- Keep the style user-friendly. Avoid excessive code references unless the document is explicitly providing code examples, such as SDK documentation.
