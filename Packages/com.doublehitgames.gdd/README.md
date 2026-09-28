# GDD Manager for Unity

Editor extension that connects a Unity project to its game design document in
[GDD Manager](https://github.com/Doublehitgames/GddApp).

> Early development.

Editor-only: the package adds nothing to player builds.

## Install

Package Manager → **Add package from git URL…**

```
https://github.com/Doublehitgames/gdd-unity.git?path=/Packages/com.doublehitgames.gdd
```

Requires Git installed on the machine. Unity 6000.0 or newer.

## The GDD window

**Window → GDD Manager** opens a dockable window with the document's page tree
and the selected page beside it. Search filters the tree by title and text.

Getting around a large document:

- **References preview first.** Clicking a reference to another page opens a
  preview of it over the current one; *Go to page* goes there.
  Ctrl+click (Cmd+click on macOS) goes straight to the page.
- **Back and forward** work as in a browser: the toolbar arrows, Alt+← / Alt+→,
  or the mouse's side buttons.
- **The trail above the title** shows where the page sits in the document;
  each step is clickable.
- Jumping to a page folds the tree down to that page's path.

### Signing in

- **Sign in with browser** opens GDD Manager's consent page; approve it and the
  window picks up by itself. The editor waits on `127.0.0.1`, ports
  47811–47815, for the browser to come back.
- **API key**: under *Other ways to connect*, paste a `gdd_sk_…` key created in
  GDD Manager → Settings → API keys.
- **CI / batchmode**: set the `GDD_API_KEY` environment variable. It is used when
  nobody signed in on that machine.

Credentials are kept in `EditorPrefs`, per person and per machine — never in a
project file. To revoke an editor's browser sign-in, use *Connected apps* in
GDD Manager's API keys settings.

### Linking the project

The first time, the window asks which GDD the Unity project belongs to and
writes the answer to `ProjectSettings/GddManager.json`. Commit that file: it is
how the rest of the team gets the same link. It holds the server URL and the
project id, nothing secret.

A self-hosted GDD Manager is set in *Other ways to connect → Server* before
linking.

## Linked pages in the Inspector

Any asset — a ScriptableObject, a prefab, a material, a scene, a folder — can
be linked to the GDD page that describes it. The Inspector of a linked asset
then shows, at the top, where the page sits in the document, its title, and
its description (folded away with one click; long pages continue in the GDD
window). *Open* shows the page in the GDD window; the **⋮** menu opens it in the
browser, links the asset to another page or unlinks it.

A prefab instance in a scene shows the page of its prefab.

To link an asset:

- In its Inspector, **Link to a page…** opens the document's tree; type to
  search every title. Several selected assets are linked at once.
- Or drag assets from the Project window onto a page in the GDD window. Each
  page lists its linked assets at the bottom; a click selects the asset.

The links are saved in `ProjectSettings/GddLinks.json`, one line per asset.
Commit that file: it is how the team shares them. Assets are keyed by guid, so
moving or renaming an asset keeps its link, and the assets themselves are never
modified. The path and page title in the file are only there to make diffs
readable.

The plugin links and shows; it never copies values from the GDD into assets.

## Planned

- A record of each build, with the packages and versions it shipped with.
