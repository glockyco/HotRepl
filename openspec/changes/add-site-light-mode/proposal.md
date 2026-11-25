# Proposal

## Why

The site only renders in dark colors, so visitors cannot use their preferred light appearance and
light screenshots require manual restyling. A real light scheme should work across the home page and
protocol reference without changing the existing dark design.

## What Changes

- Follow the system color preference until a visitor explicitly chooses a theme.
- Add a visible, keyboard-accessible light/dark toggle shared by both pages and persist explicit
  choices locally.
- Resolve the theme before first paint to avoid flashing the wrong palette.
- Add a readable light palette for all content, including code, badges, buttons, tables, and
  navigation.
- Generate both Shiki color schemes while preserving existing dark highlighting.
- Verify theme behavior and rendered appearance in a real browser and capture presentation
  screenshots.

## Capabilities

### New Capabilities

- `site-appearance`: Defines site color preference selection, persistence, first-paint behavior, and
  complete light/dark presentation.

### Modified Capabilities

None. The existing specifications do not cover the site.

## Impact

The site document, shared layout, palette, home-page styles, and both server-side highlighters
change. There are no protocol, package API, deployment, or game-runtime changes and no new runtime
dependency is required.
