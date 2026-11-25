# Design

## Context

See proposal.md for motivation. The site uses shared CSS custom properties, a shared Svelte layout,
prerendered pages, and server-side Shiki highlighting. A few home and protocol controls have literal
dark-only colors. No existing capability specifies site appearance.

## Goals / Non-Goals

- Keep theme resolution synchronous and independent of hydration.
- Reuse the existing palette structure and keep dark values unchanged.
- Keep existing content, layout, dependencies, and deployment unchanged.
- Do not add accounts, a settings page, or server-side preference storage.

## Decisions

### Resolve preferences in the document head

A small inline script reads a validated `hotrepl-theme` localStorage value and applies it to the
root element before page content is parsed. CSS follows `prefers-color-scheme` when no explicit root
theme is present, so system changes also work without JavaScript. Read and write failures are caught
because restricted storage must not break the site.

Hydration-only initialization would flash the default theme. Cookies and request-time rendering
would add server state to otherwise prerendered pages.

### Use a shared two-state toggle

The shared header contains a button labeled with its next action, for example "Switch to light
mode". CSS selects the correct label from the effective theme, avoiding a server/client preference
mismatch. Clicking reads the effective CSS color scheme, applies the opposite root theme, and
attempts to save it. Only an explicit click stores a choice.

A three-way settings menu is unnecessary for the requested binary toggle. New visitors continue to
follow the system until they choose otherwise.

### Extend existing palette tokens

Keep the current dark token values unchanged and pair them with light values using CSS
`light-dark()`. The root `color-scheme` follows the system until an explicit theme attribute selects
a scheme. Add tokens for text on accent buttons, result-code surfaces, and hover surfaces where
literal colors currently assume dark mode. Use a warm orange accent with dark enough light-mode text
and a white button foreground.

Duplicated page styles would make it easier for code blocks or badges to drift between themes. Light
text contrast is measured against actual computed surfaces, including translucent badges.

### Generate both code palettes

Use Shiki's `github-dark` and `github-light-high-contrast` themes together, with CSS `light-dark()`
selecting their token variables. Existing palette tokens still own code-block backgrounds. This
preserves server-side highlighting and avoids client-side re-highlighting on each toggle. The
standard `github-light` orange property tokens measured 3.49:1 on white, so use its high-contrast
alternative to meet AA without changing dark highlighting.

## Risks / Trade-offs

- Blocked storage prevents persistence. Catch storage errors and still switch the open page.
- Dual code colors increase static HTML size. Avoid shipping a browser highlighter or an additional
  rendering pass.
- Light surfaces can expose overlooked dark literals. Inspect both pages, expanded schemas,
  responsive controls, and all computed light text contrast in the browser.
- The existing dark theme may have contrast limitations. Preserve its design as explicitly required
  and apply the AA requirement to the new light theme.

## Migration Plan

No data migration or deployment is needed for local verification. Reverting the site changes
restores the previous dark-only presentation. Do not push or deploy as part of this change.
