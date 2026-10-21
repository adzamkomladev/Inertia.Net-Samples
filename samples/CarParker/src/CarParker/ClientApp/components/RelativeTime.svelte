<script lang="ts">
  import { onMount } from 'svelte'

  let { iso, class: className }: { iso: string; class?: string } = $props()

  // Absolute on the server and first client render (hydration-safe); relative after mount for < ~3 months.
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const absolute = $derived(new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(iso)))

  const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
    ['month', 2_592_000], ['week', 604_800], ['day', 86_400], ['hour', 3_600], ['minute', 60],
  ]

  const text = $derived.by(() => {
    if (!mounted) return absolute
    const diff = (new Date(iso).getTime() - Date.now()) / 1000
    if (Math.abs(diff) > 7_776_000) return absolute
    const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
    for (const [unit, secs] of UNITS) if (Math.abs(diff) >= secs) return rtf.format(Math.trunc(diff / secs), unit)
    return 'just now'
  })
</script>

<time datetime={iso} title={absolute} class={className}>{text}</time>
