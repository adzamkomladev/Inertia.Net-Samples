<script lang="ts">
  import { money } from '@/lib/money'
  import { cn } from '@/lib/utils'

  /** Prices and counters always render in IBM Plex Mono with tabular figures. */
  let {
    cents,
    class: className,
    size = 'md',
    muted = false,
  }: {
    cents: number | null | undefined
    class?: string
    size?: 'sm' | 'md' | 'lg' | 'xl'
    muted?: boolean
  } = $props()

  const sizes = {
    sm: 'text-[13px]',
    md: 'text-sm',
    lg: 'text-lg',
    xl: 'text-2xl font-medium tracking-tight',
  }
</script>

<span class={cn('font-mono tabular-nums', sizes[size], muted && 'text-muted-foreground', className)}>{money(cents)}</span>
