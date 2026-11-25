# Tasks

## 1. Site appearance

- [x] 1.1 Implement pre-paint preference resolution, a shared persistent accessible toggle, complete
      light palette, and dual code highlighting. Verify system preference, explicit override,
      navigation, reload, storage failure, and both pages in a real browser.

## 2. Integration verification

- [x] 2.1 Run the site type check, production build, applicable non-Nix checks, and OpenSpec
      validation. Inspect both schemes at desktop and mobile sizes, measure light text contrast, and
      save the requested three light screenshots at 1440x900 with device scale factor 2. Record
      results in this change.

## Verification results

- `bun install --frozen-lockfile` and the protocol workspace build passed.
- `bun run --cwd site check` passed with zero errors and warnings.
- `bun run --cwd site build` passed, including prerendering and protocol example validation.
- Targeted `dprint check` and `openspec validate add-site-light-mode --strict` passed.
- Chromium inspection covered both pages and schemes at 1440x900, device scale factor 2, and mobile
  layouts at 390x844. Expanded protocol schemas also passed light contrast measurement.
- Light text contrast minimums were 4.86:1 across 183 home-page text elements and 5.74:1 across
  4,932 protocol text elements with schemas expanded.
- Browser checks passed for system defaults, live system changes, keyboard toggling, local
  persistence, navigation, reload, invalid preferences, blocked storage, and CSS without JavaScript.
- Paint observers on both pages recorded the saved dark override at first paint and first contentful
  paint while the emulated system preference was light.
- Final light captures are `home.png`, `quickstart.png`, and `protocol.png` in
  `C:/Users/jglock/AppData/Local/Temp/hotrepl-light/`. Dark desktop captures are saved beside them.
- Nix is unavailable on this Windows host, so the full Nix gate could not run.
- Existing mobile protocol behavior remains out of scope: open the navigation sheet and select a
  message. The sheet stays open and body scrolling stays locked until Close navigation is pressed.
