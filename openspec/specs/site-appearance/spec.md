# site-appearance Specification

## Purpose

Defines how the project site respects color preferences and presents readable, complete light and
dark appearances across its pages.

## Requirements

### Requirement: Follow system appearance by default

The site SHALL use the system color preference when no valid explicit choice is stored, including
changes to that preference while the page is open.

#### Scenario: First visit in light mode

- **WHEN** a visitor has no saved theme and their system prefers light
- **THEN** the site renders in light mode

#### Scenario: System appearance changes

- **WHEN** the system color preference changes without an explicit site choice
- **THEN** the open page updates to match it

#### Scenario: Invalid or unavailable storage

- **WHEN** saved preferences cannot be read or do not name a supported theme
- **THEN** the site follows the system preference without breaking navigation or rendering

### Requirement: Offer a persistent accessible theme toggle

Every page SHALL expose a keyboard-accessible control to switch between light and dark. Its
accessible name SHALL identify the action. An explicit selection SHALL override the system
preference and persist across navigation and reload when local storage is available.

#### Scenario: Choose a different theme

- **WHEN** a visitor activates the theme toggle
- **THEN** the page switches to the other theme
- **AND** the accessible name updates to identify the next action

#### Scenario: Preserve the selected theme

- **WHEN** a visitor navigates, reloads, or changes their system preference after choosing a theme
- **THEN** the site retains the explicit choice

#### Scenario: Storage cannot be written

- **WHEN** a visitor toggles the theme while local storage is unavailable
- **THEN** the open page still switches themes without an uncaught error

### Requirement: Resolve appearance before first paint

The site SHALL apply the effective color scheme before visible content is first painted, including
when a stored selection differs from the system preference.

#### Scenario: Load an explicit override

- **WHEN** a visitor loads either page with a saved dark selection on a light system
- **THEN** the first visible rendering uses the dark palette without a light flash

### Requirement: Render complete readable color schemes

The home page and protocol reference SHALL render all content in both color schemes, including
syntax highlighting, navigation, badges, buttons, and any tables. Light-mode text SHALL meet WCAG AA
contrast. The existing dark palette and code colors SHALL remain unchanged.

#### Scenario: Inspect light content

- **WHEN** either page is viewed in light mode
- **THEN** text and syntax highlighting remain readable against light surfaces
- **AND** controls, examples, schemas, and responsive navigation retain their structure and function

#### Scenario: Inspect dark content

- **WHEN** either page is viewed in dark mode
- **THEN** the existing dark colors and code highlighting are preserved
