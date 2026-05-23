<svelte:head>
  <title>HotRepl Protocol Reference</title>
  <meta
    name="description"
    content="Protocol reference for HotRepl's runtime C# REPL and typed command bridge."
  />
</svelte:head>

<section class="protocol-reference">
  <p class="eyebrow">Protocol Reference</p>
  <h1>HotRepl protocol reference</h1>
  <p>
    Use <code>@hotrepl/protocol</code> for TypeScript message types and schemas shared by the SDK,
    CLI, MCP server, and conformance tests. The package is sourced from this Bun workspace so
    site builds consume the same protocol contract as the runtime tooling.
  </p>
  <a href="https://github.com/glockyco/HotRepl/tree/main/packages/protocol" rel="noopener noreferrer">
    View the protocol package on GitHub
  </a>
</section>

<style>
  .protocol-reference {
    max-width: 680px;
    padding: 72px 0;
  }

  .eyebrow {
    color: var(--accent);
    font-size: 0.875rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    margin-bottom: 12px;
    text-transform: uppercase;
  }

  h1 {
    color: var(--text);
    font-size: clamp(2rem, 5vw, 3.5rem);
    font-weight: 900;
    letter-spacing: -0.04em;
    line-height: 1;
    margin-bottom: 20px;
  }

  p {
    color: var(--muted);
    font-size: 1rem;
    line-height: 1.7;
    margin-bottom: 24px;
  }

  code {
    background: var(--code-bg);
    border-radius: 4px;
    color: var(--text);
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.875rem;
    padding: 2px 6px;
  }

  a {
    color: var(--accent);
    font-weight: 700;
    text-decoration: underline;
    text-underline-offset: 3px;
  }
</style>
