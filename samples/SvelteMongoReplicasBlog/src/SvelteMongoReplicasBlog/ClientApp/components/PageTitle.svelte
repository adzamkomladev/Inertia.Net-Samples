<script lang="ts">
  import { usePage } from '@inertiajs/svelte'

  let { title, description }: { title?: string; description?: string } = $props()

  const appName = $derived((usePage().props as { appName?: string }).appName ?? 'Inkwell')
  const text = $derived(title ? `${title} · ${appName}` : appName)
</script>

<svelte:head>
  <title>{text}</title>
  {#if description}<meta name="description" content={description} />{/if}
</svelte:head>
