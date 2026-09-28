# Changelog

## [Unreleased]

- GDD window (**Window → GDD Manager**): page tree with search, the selected
  page's description rendered from markdown, `$[Page]` and `$[#id]` references
  as links, and *Open in browser*.
- Navigation for large documents: a reference opens a preview with *Go to page*
  (Ctrl/Cmd+click jumps), back/forward history (toolbar, Alt+←/→, mouse side
  buttons), and a trail of parent pages above the title.
- Sign-in through the browser (OAuth 2.1 + PKCE, loopback redirect), a pasted
  API key, or `GDD_API_KEY` from the environment. Credentials stay in
  `EditorPrefs`.
- The Unity project ↔ GDD link is saved to `ProjectSettings/GddManager.json`,
  meant to be committed.
- Assets linked to GDD pages: the Inspector of a linked asset shows the page's
  trail, title and description (foldable), with *Open* in the GDD window; a
  prefab instance shows its prefab's page. Link from the Inspector with a
  searchable page picker, or by dragging assets onto a page in the GDD window,
  which lists each page's linked assets. Links live in
  `ProjectSettings/GddLinks.json`, keyed by guid, one line per asset.

## [0.0.1] - 2026-09-28

- Package skeleton. No features yet.
