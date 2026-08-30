# UI mockups

These ImageGen outputs are layout references for the WPF implementation, not pixel-perfect
acceptance images. Each was generated independently as a realistic, shippable 16:9 Windows desktop
application using compact Segoe-style typography, graphite/slate panels, restrained orange and cyan
accents, docked panes, no browser chrome, no logos/trademarks, and no watermark.

- `01-main-workspace.png`: Project/Scene docks, textured track viewport, selected object and transform
  gizmo, Inspector, Problems, Jobs, and staged-build action. A targeted edit removed branded boards.
- `02-route-ai-editor.png`: orthographic centreline/lane handles, checkpoints, start grid, cameras,
  route tree, property editor, and elevation profile.
- `03-material-collision.png`: semantic collision overlay, material browser, raw source shader identity,
  approximation label, UV selector, channel tiles, and validation warnings.
- `04-publish-validation.png`: read-only source versus staged diff, validation checklist, blocked CQTC
  write, changed files, manifest, backup notice, and publish actions. A targeted edit removed the app
  symbol and branded circuit name.

The selected images were generated with the built-in `image_gen` tool using `ui-mockup` prompts.
