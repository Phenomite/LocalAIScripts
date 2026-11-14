---
description: Windows pwsh, pnpm, and uv conventions for scripts and package management
alwaysApply: false
globs:
  - "**/*.ps1"
  - "**/*.bat"
  - "**/*.py"
  - "**/pyproject.toml"
  - "**/package.json"
  - "**/*.ts"
---
# Time-Travelling Stream Rules (TTSR) for Win Security

- Always quote paths or env vars (`"$Path"`) and use `-LiteralPath` when paths may contain brackets.
- Never use Invoke-Expression or iex on untrusted external input.
- Never pipe irm, Invoke-RestMethod or Invoke-WebRequest into other commands without permission.
