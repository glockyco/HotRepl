// Conventional Commits ruleset, copied verbatim from
// @commitlint/config-conventional (conventional-changelog org).
//
// Why inline (no `extends`):
//   - The brew formula for commitlint does NOT bundle
//     `@commitlint/config-conventional`, so `extends: ['@commitlint/config-conventional']`
//     would require contributors to install Node modules separately.
//   - The ruleset is small and stable; vendoring it keeps the brew install
//     path zero-dep and makes the policy auditable in one place.
//
// Severity values: 0 = disabled, 1 = warning, 2 = error.

module.exports = {
  rules: {
    "body-leading-blank": [1, "always"],
    "body-max-line-length": [2, "always", 100],
    "footer-leading-blank": [1, "always"],
    "footer-max-line-length": [2, "always", 100],
    "header-max-length": [2, "always", 100],
    "header-trim": [2, "always"],
    "subject-case": [
      2,
      "never",
      ["sentence-case", "start-case", "pascal-case", "upper-case"],
    ],
    "subject-empty": [2, "never"],
    "subject-full-stop": [2, "never", "."],
    "type-case": [2, "always", "lower-case"],
    "type-empty": [2, "never"],
    "type-enum": [
      2,
      "always",
      [
        "build",
        "chore",
        "ci",
        "docs",
        "feat",
        "fix",
        "perf",
        "refactor",
        "revert",
        "style",
        "test",
      ],
    ],
  },
};
