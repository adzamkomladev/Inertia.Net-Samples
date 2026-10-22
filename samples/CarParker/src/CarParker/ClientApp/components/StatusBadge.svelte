<script lang="ts">
  import { statusLabel, statusTone, toneClasses, toneDot } from '@/lib/status'
  import { cn } from '@/lib/utils'

  /** A status string straight from the API ("paid", "noshow", "requested", …). */
  let {
    status,
    label,
    dot = true,
    size = 'sm',
    class: className,
  }: {
    status: string | null | undefined
    label?: string
    dot?: boolean
    size?: 'xs' | 'sm'
    class?: string
  } = $props()

  const tone = $derived(statusTone(status))
</script>

<span
  class={cn(
    'inline-flex w-fit items-center gap-1.5 rounded-full font-medium ring-1 ring-inset whitespace-nowrap',
    size === 'xs' ? 'px-1.5 py-0.5 text-xs' : 'px-2 py-0.5 text-xs',
    toneClasses[tone],
    className,
  )}
>
  {#if dot}<span class={cn('size-1.5 shrink-0 rounded-full', toneDot(tone))} aria-hidden="true"></span>{/if}
  {label ?? statusLabel(status)}
</span>
