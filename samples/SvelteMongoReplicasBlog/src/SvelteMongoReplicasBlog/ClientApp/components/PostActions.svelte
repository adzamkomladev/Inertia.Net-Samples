<script lang="ts">
  import { Ellipsis, Archive, ArchiveRestore } from '@lucide/svelte'
  import DropdownMenu, { type DropdownEntry } from '@/components/ui/dropdown-menu/DropdownMenu.svelte'
  import { useSimpleToggle } from '@/lib/social-toggle.svelte'
  import type { PostSummary } from '@/types'

  let { post }: { post: PostSummary } = $props()
  const archive = useSimpleToggle(() => `/posts/${post.id}/archive`, () => !!post.archivedAt)

  const entries = $derived<DropdownEntry[]>([
    {
      type: 'item',
      label: post.archivedAt ? 'Restore post' : 'Archive post',
      icon: post.archivedAt ? ArchiveRestore : Archive,
      disabled: archive.pending,
      onSelect: archive.toggle,
    },
  ])
</script>

{#if post.canArchive}
  <DropdownMenu {entries} align="end" contentClass="w-44" triggerLabel="More post actions" triggerClass="text-muted-foreground hover:bg-accent inline-flex size-8 items-center justify-center rounded-full">
    {#snippet trigger()}
      <Ellipsis class="size-4" />
    {/snippet}
  </DropdownMenu>
{/if}
