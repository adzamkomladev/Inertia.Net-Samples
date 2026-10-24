<script lang="ts">
  import { money } from '@/lib/money'
  import { cn } from '@/lib/utils'

  /** Money in the display face with tabular figures, so columns of prices line up. */
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
    sm: 'text-sm',
    md: 'text-base',
    lg: 'text-xl',
    xl: 'text-3xl',
  }
</script>

<span class={cn('font-display font-semibold tabular-nums', sizes[size], muted && 'text-muted-foreground', className)}>{money(cents)}</span>
