<script lang="ts">
  interface Family {
    id: string;
    name: string;
    messages: { type: string }[];
  }

  interface Props {
    families: Family[];
  }

  let { families }: Props = $props();
</script>

<nav class="proto-nav" aria-label="Protocol reference navigation">
  <div class="nav-header">
    <a class="back-link" href="/">← HotRepl</a>
    <span class="nav-title">Protocol Reference</span>
  </div>

  {#each families as family (family.id)}
    <div class="family-group">
      <a class="family-name" href="#{family.messages[0]?.type ?? family.id}">{family.name}</a>
      <ul class="msg-list">
        {#each family.messages as msg (msg.type)}
          <li>
            <a class="msg-link" href="#{msg.type}">{msg.type}</a>
          </li>
        {/each}
      </ul>
    </div>
  {/each}

  <div class="family-group">
    <a class="family-name" href="#shared-types">Shared Types</a>
  </div>
</nav>

<style>
  .proto-nav {
    width: 240px;
    flex-shrink: 0;
    position: sticky;
    top: 52px; /* height of site header */
    height: calc(100dvh - 52px);
    overflow-y: auto;
    padding: 20px 0;
    border-right: 1px solid var(--border);
    scrollbar-width: thin;
  }

  .nav-header {
    padding: 0 16px 16px;
    border-bottom: 1px solid var(--border);
    margin-bottom: 12px;
  }

  .back-link {
    display: block;
    font-size: 0.8125rem;
    color: var(--muted);
    text-decoration: none;
    margin-bottom: 8px;
  }

  .back-link:hover {
    color: var(--text);
  }

  .nav-title {
    font-size: 0.8125rem;
    font-weight: 700;
    color: var(--text);
    letter-spacing: 0.02em;
  }

  .family-group {
    margin-bottom: 4px;
  }

  .family-name {
    display: block;
    padding: 5px 16px;
    font-size: 0.8125rem;
    font-weight: 700;
    color: var(--text);
    text-decoration: none;
    letter-spacing: 0.01em;
  }

  .family-name:hover {
    color: var(--accent);
  }

  .msg-list {
    list-style: none;
    margin: 0;
    padding: 0;
  }

  .msg-link {
    display: block;
    padding: 3px 16px 3px 28px;
    font-size: 0.8125rem;
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    color: var(--muted);
    text-decoration: none;
    transition: color 0.1s;
  }

  .msg-link:hover {
    color: var(--accent);
  }
</style>
