---
description: Interrupt speculation in thinking or prose and force web_search verification
condition: "(?i)\\b(I think the (flag|syntax|option|parameter) is|I'm not sure (if|whether|about the exact)|I don't (recall|remember) the exact|assuming the API|let me guess|probably takes the flag|think flag is|Need not guess)\\b"
scope: [thinking, text]
interruptMode: prose-only
---
# STOP SPECULATING. Don't hedge syntax, flags, or APIs

1. For local code, run `grep`, `read`, or `--help` via `pwsh`.
2. For external libraries, CLI tools, or docs, call `web_search` or read url immediately before writing code.
