# Contributing to DotCraft

This guide explains how to contribute to DotCraft using the development guidelines.

## Two Ways to Contribute

### Option 1: Using AI-Powered Tools

If you're using AI coding assistants (like CodeBuddy, Claude, or similar tools), the development guidelines are available as a **Skill** located in `samples/skills/dev-guide/`.

**How it works**:
1. The AI tool loads the Skill from `samples/skills/dev-guide/SKILL.md` and `samples/skills/dev-guide/references/` files
2. When you ask for help with DotCraft development, the AI follows the guidelines
3. The AI references actual code files and documentation as needed

**Example prompts**:
- "Help me create a new Discord channel module"
- "Add a new tool for generating reports"
- "Review this code for DotCraft style compliance"

The AI will:
- Follow C# coding conventions
- Place code in the correct module
- Reference existing implementations
- Suggest bilingual documentation

### Option 2: Manual Development

If you prefer to work without AI assistance, simply read the guidelines directly from `samples/skills/dev-guide/`:

1. **Start here**: `samples/skills/dev-guide/SKILL.md` - Core development guidelines
2. **Architecture reference**: `samples/skills/dev-guide/references/architecture.md` - File and documentation index
3. **Module development**: `samples/skills/dev-guide/references/module-development.md` - Step-by-step guide with file references

**Quick reference**:
- **Code style**: See `samples/skills/dev-guide/SKILL.md` → Code Style Guidelines
- **Where to add features**: See `samples/skills/dev-guide/SKILL.md` → Architecture Guidelines → Where to Add New Features
- **Create a module**: See `samples/skills/dev-guide/references/module-development.md`
- **Existing examples**: Check `src/DotCraft.QQ/` (full module) or `src/DotCraft.Unity/` (minimal module)

## What's Covered

The guidelines include:

- ✅ **C# Code Style** - Official conventions with modern features
- ✅ **Architecture Patterns** - Module structure and placement decisions
- ✅ **Module Development** - Complete process with references to existing code
- ✅ **Documentation Requirements** - Bilingual (English + Chinese) standards
- ✅ **Development Workflow** - Before, during, and after making changes

## Quick Checklist

Before submitting your contribution:

- [ ] Follows C# official style conventions
- [ ] Code placed in correct module (Core, App, or channel module)
- [ ] XML documentation comments added for public APIs
- [ ] Documentation provided in both English and Chinese
- [ ] Configuration validated with appropriate error messages
- [ ] Tested manually in real environment

## Need Help?

- **Development guidelines**: Check `samples/skills/dev-guide/`
- **Code examples**: Check existing modules in `src/`
- **Documentation**: Check `docs/` for user guides
- **Samples**: Check `samples/` for complete examples
- **Questions**: Open an issue on GitHub

## License

By contributing to DotCraft, you agree that your contributions will be licensed under the Apache License 2.0.
