<script lang="ts">
  import { Moon, Sun } from '@lucide/svelte'
  import { applyTheme } from '@/lib/theme.svelte'
  import { cn } from '@/lib/utils'

  /** Light/dark toggle for the deck shell; persists via the same `theme` cookie the server reads. */
  let { class: className }: { class?: string } = $props()

  const read = () => (typeof document !== 'undefined' && document.documentElement.classList.contains('dark'))
  let dark = $state(false)
  // Match the server-set class after hydration (SSR renders both icons, one hidden).
  $effect(() => { dark = read() })

  const toggle = () => {
    dark = !dark
    applyTheme(dark ? 'dark' : 'light')
  }
</script>

<button
  type="button"
  onclick={toggle}
  aria-label={dark ? 'Switch to light theme' : 'Switch to dark theme'}
  class={cn(
    'text-muted-foreground hover:text-foreground hover:bg-accent inline-flex size-9 items-center justify-center rounded-xl transition-all active:scale-90',
    className,
  )}
>
  <Sun class={cn('size-[18px]', dark && 'hidden')} aria-hidden="true" />
  <Moon class={cn('hidden size-[18px]', !dark && 'hidden')} aria-hidden="true" />
</button>
