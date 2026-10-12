<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  let {
    label,
    count,
    active = false,
    tone,
    disabled = false,
    href,
    onclick,
    children,
  }: {
    label: string
    count?: number
    active?: boolean
    tone: 'comment' | 'repost' | 'like' | 'bookmark'
    disabled?: boolean
    href?: string
    onclick?: () => void
    children?: Snippet
  } = $props()

  const tones = {
    comment: { hover: 'hover:text-comment [&:hover>span:first-child]:bg-comment/10', active: 'text-comment' },
    repost: { hover: 'hover:text-repost [&:hover>span:first-child]:bg-repost/10', active: 'text-repost' },
    like: { hover: 'hover:text-like [&:hover>span:first-child]:bg-like/10', active: 'text-like' },
    bookmark: { hover: 'hover:text-bookmark [&:hover>span:first-child]:bg-bookmark/10', active: 'text-bookmark' },
  }
  // Only animate after a click, never when an active control first renders.
  let bumped = $state(false)
  const rootClass = $derived(cn(
    '-ml-2 flex items-center rounded-full pr-1 text-muted-foreground transition-colors disabled:pointer-events-none disabled:opacity-50',
    tones[tone].hover,
    active && tones[tone].active,
  ))
  const iconClass = $derived(cn(
    'flex size-8 items-center justify-center rounded-full transition-colors [&_svg]:size-[18px]',
    active && '[&_svg]:fill-current',
    bumped && active && '[&_svg]:animate-pop',
  ))
  const click = () => { bumped = true; onclick?.() }
</script>

{#snippet inner()}
  <span class={iconClass} onanimationend={() => (bumped = false)}>{@render children?.()}</span>
  <span class="min-w-5 overflow-hidden text-left text-[13px] tabular-nums">
    {#if count}<span class="animate-tick inline-block">{count}</span>{/if}
  </span>
  <span class="sr-only">{label}</span>
{/snippet}

{#if href}
  <Link {href} class={rootClass} aria-label={label}>{@render inner()}</Link>
{:else}
  <button type="button" class={rootClass} aria-label={label} aria-pressed={active} {disabled} onclick={click}>{@render inner()}</button>
{/if}
