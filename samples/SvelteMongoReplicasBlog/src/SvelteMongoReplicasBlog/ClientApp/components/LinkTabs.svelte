<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import { cn } from '@/lib/utils'

  let { tabs, current, label }: { tabs: { href: string; label: string; value: string }[]; current: string; label: string } = $props()
</script>

<!-- Underlined link tabs (feed modes, profile sections). -->
<nav aria-label={label} class="flex gap-6 border-b">
  {#each tabs as tab (tab.value)}
    <Link
      href={tab.href}
      preserveScroll
      aria-current={tab.value === current ? 'page' : undefined}
      class={cn(
        'text-muted-foreground hover:text-foreground relative -mb-px py-3 text-[15px] transition-colors',
        'after:bg-foreground after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:origin-center after:scale-x-0 after:rounded-full after:transition-transform after:duration-300',
        tab.value === current && 'text-foreground font-medium after:scale-x-100',
      )}
    >
      {tab.label}
    </Link>
  {/each}
</nav>
