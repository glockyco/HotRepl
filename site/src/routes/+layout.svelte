<script lang="ts">
  import "../app.css";
  let { children } = $props();
</script>

<div class="site-shell">
  <header class="site-header">
    <nav class="nav-inner" aria-label="Main navigation">
      <a class="wordmark" href="/">HotRepl</a>
      <a class="nav-link" href="/#quickstart">Quickstart</a>
      <a
        class="nav-link"
        href="https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md"
        rel="noopener noreferrer"
      >
        Authoring Commands
      </a>
      <a class="nav-link" href="/protocol/">Protocol</a>
      <a class="nav-link" href="https://github.com/glockyco/HotRepl" rel="noopener noreferrer">
        GitHub
      </a>
    </nav>
  </header>

  <main class="site-main">
    {@render children()}
  </main>

  <footer class="site-footer">
    <span>By <a href="https://glockyco.com/">Johann Glock</a></span>
    <span>·</span>
    <a href="https://github.com/glockyco/HotRepl">GitHub</a>
  </footer>
</div>

<style>
  .site-shell {
    min-height: 100dvh;
    display: flex;
    flex-direction: column;
  }

  .site-header {
    border-bottom: 1px solid var(--border);
    position: sticky;
    top: 0;
    background: var(--bg);
    z-index: 10;
  }

  .nav-inner {
    max-width: 960px;
    margin: 0 auto;
    padding: 0 24px;
    height: 52px;
    display: flex;
    align-items: center;
    gap: 24px;
  }

  .wordmark {
    font-size: 1rem;
    font-weight: 800;
    color: var(--accent);
    letter-spacing: -0.02em;
    text-decoration: none;
    margin-right: auto;
  }

  .nav-link {
    font-size: 0.875rem;
    font-weight: 600;
    color: var(--muted);
    text-decoration: none;
    transition: color 0.15s;
  }

  .nav-link:hover {
    color: var(--text);
  }

  .site-main {
    flex: 1;
    max-width: 960px;
    margin: 0 auto;
    padding: 0 24px;
    width: 100%;
  }

  .site-footer {
    border-top: 1px solid var(--border);
    padding: 20px 24px;
    display: flex;
    justify-content: center;
    gap: 12px;
    font-size: 0.8125rem;
    color: var(--muted);
  }

  .site-footer a {
    color: var(--muted);
    text-decoration: underline;
    text-underline-offset: 3px;
  }

  .site-footer a:hover {
    color: var(--text);
  }
</style>
