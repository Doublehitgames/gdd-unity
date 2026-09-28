# Changelog

## [Unreleased]

- GDD window (**Window → GDD Manager**): page tree with search, the selected
  page's description rendered from markdown, `$[Page]` references as links, and
  *Open in browser*.
- Sign-in through the browser (OAuth 2.1 + PKCE, loopback redirect), a pasted
  API key, or `GDD_API_KEY` from the environment. Credentials stay in
  `EditorPrefs`.
- The Unity project ↔ GDD link is saved to `ProjectSettings/GddManager.json`,
  meant to be committed.

## [0.0.1] - 2026-09-28

- Package skeleton. No features yet.
