<script lang="ts">
  interface Props {
    type: string;
    direction: "C→S" | "S→C";
    description: string;
    exampleHtml: string;
    schemaHtml: string;
  }
  let { type, direction, description, exampleHtml, schemaHtml }: Props = $props();
</script>

<article class="card" id={type}>
  <div class="card-header">
    <code class="type-badge">{type}</code>
    <span class="direction-badge" class:cs={direction === "C→S"} class:sc={direction === "S→C"}>
      {direction}
    </span>
    <span class="version-tag">v2</span>
  </div>

  <p class="description">{description}</p>

  <div class="block-label">Example</div>
  <!-- eslint-disable-next-line svelte/no-at-html-tags -->
  {@html exampleHtml}

  <details class="schema-details">
    <summary>JSON Schema</summary>
    <!-- eslint-disable-next-line svelte/no-at-html-tags -->
    {@html schemaHtml}
  </details>
</article>

<style>
  .card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px 24px;
    margin-bottom: 16px;
    scroll-margin-top: 72px;
  }

  .card-header {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 12px;
    flex-wrap: wrap;
  }

  .type-badge {
    font-family: ui-monospace, "Cascadia Code", "Fira Code", monospace;
    font-size: 0.9375rem;
    font-weight: 700;
    color: var(--accent);
    background: var(--accent-dim);
    padding: 3px 10px;
    border-radius: 4px;
  }

  .direction-badge {
    font-size: 0.75rem;
    font-weight: 700;
    padding: 2px 8px;
    border-radius: 4px;
    letter-spacing: 0.02em;
  }

  .direction-badge.cs {
    background: oklch(0.75 0.18 45 / 20%);
    color: var(--badge-cs);
  }

  .direction-badge.sc {
    background: oklch(0.65 0.15 220 / 20%);
    color: var(--badge-sc);
  }

  .version-tag {
    font-size: 0.75rem;
    color: var(--muted);
    margin-left: auto;
  }

  .description {
    font-size: 0.875rem;
    color: var(--muted);
    line-height: 1.6;
    margin-bottom: 16px;
  }

  .block-label {
    font-size: 0.75rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
    margin-bottom: 8px;
  }

  .schema-details {
    margin-top: 12px;
  }

  .schema-details summary {
    font-size: 0.8125rem;
    font-weight: 600;
    color: var(--muted);
    cursor: pointer;
    padding: 6px 0;
    user-select: none;
    list-style: none;
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .schema-details summary::before {
    content: "▶";
    font-size: 0.625rem;
    transition: transform 0.15s;
  }

  .schema-details[open] summary::before {
    transform: rotate(90deg);
  }

  .schema-details summary:hover {
    color: var(--text);
  }

  .schema-details :global(.shiki) {
    margin-top: 8px;
  }
</style>
