# 0002 — Build a custom layout engine

Status: Accepted

Date: 2026-09-22

## Context

The formatter needs width-aware line breaks, indentation, configurable clause layouts and stable comment placement. Parsing determines what the source means; rendering determines how it fits on lines. The same formatting behavior must serve CLI and editor integrations.

## Decision

Formatting rules in Core will convert supported ScriptDom fragments and their source tokens into a small document model. The model will include text, concatenation, conditional and forced line breaks, indentation and groups. A separate renderer will turn that model into text using width, indentation and line-ending options. The renderer will not depend on ScriptDom or editor APIs.

Rules must preserve semantic tokens, comments and literals. Unsupported or unsafe cases should return the original text with a diagnostic. Output must be checked by golden tests for exact text and idempotency.

## Consequences

- Rules can express layout choices without duplicating line-width logic.
- The extra document-model layer adds implementation work and requires dedicated renderer tests.
- Formatting rules, trivia attachment and rendering can evolve independently, but their combined output must remain parseable and stable.

## Alternatives

- Writing SQL directly from each AST visitor into a string builder couples syntax handling to line wrapping.
- Using the ScriptDom generator as the formatter limits the planned style options.
- Regex replacement cannot safely distinguish SQL structure from comments and literals.
