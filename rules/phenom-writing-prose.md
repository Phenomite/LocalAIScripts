---
description: "Writing style: required lines only, per-item brevity, agent-agnostic, imperative"
alwaysApply: true
---
# Writing Prose

All writing: steering, rules, skills, operator responses, and agent-facing prose.

## Lines

- One action or required fact per line; cut rationale, history, dates, verification notes, and asides.
- Use imperative mood and ordered steps where order matters; write so a fresh agent acts correctly the first time.
- Use parentheses and semicolons instead of extra sentences (put traps, causes, and exceptions inside).
- Name the actor ("Tool sets a UA that empties `field`", not "an override is set").
- Abbreviate repeated terms after first use; contractions as-is.

## Detail

- Keep the fix and the check; drop the symptom catalogue.
- Use minimum identifiers: relative paths and the selector or argument that changes an outcome (no selector dumps, host lists, or category list).
- Prefer plain words over domain jargon ("costs" not "medians").
- State the general action, not the trigger (what to do for x, not only when y does z).
- Stay agnostic wherever possible: no account names, place names, IDs, or session references; state how to obtain a value.

## Structure

- Give tool and subjects a heading and a group; prefer formats like `Subject: tool (trap)`.
- Keep code fences flush, never nested in lists, `markdownlint-cli2` can help.
- Use bullets only for parallel items; run single instructions as prose.
- Quote error strings, put the cause in parentheses, then state the action.
- Edit one coherent file rather than appending notes; supersede and delete the old.

## Size

- Follow the idiom: "Why use many words when few do trick?" whilst ensuring sentence structure remains readable.
- Compress until removing any line loses its meaning or action.
