<script lang="ts">
  import { TimeField } from 'bits-ui'
  import { parseTime } from '@internationalized/date'
  import Clock from '@lucide/svelte/icons/clock'

  let { value = $bindable(''), id, label, required = false }: {
    value?: string; id: string; label: string; required?: boolean
  } = $props()
</script>

<TimeField.Root value={value ? parseTime(value) : undefined} onValueChange={time => value = time?.toString().slice(0, 5) ?? ''} hourCycle={24} granularity="minute" {required}>
  <TimeField.Label class="sr-only">{label}</TimeField.Label>
  <TimeField.Input {id} class="bg-card ring-input focus-within:ring-ring flex h-11 w-full items-center rounded-lg px-3 ring-1 ring-inset focus-within:ring-2 md:h-10">
    {#snippet children({ segments })}
      <Clock class="text-muted-foreground mr-2 size-4" aria-hidden="true" />
      {#each segments as segment, index (index)}
        <TimeField.Segment part={segment.part} class="focus:bg-accent rounded px-0.5 py-1 text-sm outline-none">{segment.value}</TimeField.Segment>
      {/each}
    {/snippet}
  </TimeField.Input>
</TimeField.Root>
