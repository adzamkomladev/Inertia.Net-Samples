<script lang="ts">
  import { DatePicker } from 'bits-ui'
  import { parseDate } from '@internationalized/date'
  import CalendarDays from '@lucide/svelte/icons/calendar-days'
  import ChevronLeft from '@lucide/svelte/icons/chevron-left'
  import ChevronRight from '@lucide/svelte/icons/chevron-right'

  let { value = $bindable(''), id, label, min, max, required = false }: {
    value?: string; id: string; label: string; min?: string; max?: string; required?: boolean
  } = $props()
</script>

<DatePicker.Root
  value={value ? parseDate(value) : undefined}
  onValueChange={date => value = date?.toString() ?? ''}
  minValue={min ? parseDate(min) : undefined}
  maxValue={max ? parseDate(max) : undefined}
  {required}
  weekStartsOn={1}
  fixedWeeks
>
  <DatePicker.Label class="sr-only">{label}</DatePicker.Label>
  <DatePicker.Input {id} class="bg-card ring-input focus-within:ring-ring flex h-11 w-full items-center rounded-lg px-3 ring-1 ring-inset focus-within:ring-2 md:h-10">
    {#snippet children({ segments })}
      {#each segments as segment, index (index)}
        <DatePicker.Segment part={segment.part} class="focus:bg-accent rounded px-0.5 py-1 text-sm outline-none">{segment.value}</DatePicker.Segment>
      {/each}
      <DatePicker.Trigger aria-label="Open calendar" class="hover:bg-accent ml-auto flex size-8 items-center justify-center rounded-md transition-colors">
        <CalendarDays class="size-4" />
      </DatePicker.Trigger>
    {/snippet}
  </DatePicker.Input>
  <DatePicker.Portal>
    <DatePicker.Content sideOffset={6} class="bg-popover text-popover-foreground shadow-pop ring-border data-[state=open]:animate-in data-[state=open]:fade-in-0 z-50 rounded-xl p-3 ring-1">
      <DatePicker.Calendar>
        {#snippet children({ months, weekdays })}
          <DatePicker.Header class="mb-3 flex items-center justify-between">
            <DatePicker.PrevButton aria-label="Previous month" class="hover:bg-accent flex size-9 items-center justify-center rounded-md"><ChevronLeft class="size-4" /></DatePicker.PrevButton>
            <DatePicker.Heading class="text-sm font-semibold" />
            <DatePicker.NextButton aria-label="Next month" class="hover:bg-accent flex size-9 items-center justify-center rounded-md"><ChevronRight class="size-4" /></DatePicker.NextButton>
          </DatePicker.Header>
          {#each months as month (month.value.toString())}
            <DatePicker.Grid class="border-collapse">
              <DatePicker.GridHead><DatePicker.GridRow>
                {#each weekdays as day, index (index)}<DatePicker.HeadCell class="text-muted-foreground size-9 text-xs font-normal">{day}</DatePicker.HeadCell>{/each}
              </DatePicker.GridRow></DatePicker.GridHead>
              <DatePicker.GridBody>
                {#each month.weeks as week (week[0].toString())}
                  <DatePicker.GridRow>
                    {#each week as date (date.toString())}
                      <DatePicker.Cell {date} month={month.value} class="p-0">
                        <DatePicker.Day class="hover:bg-accent focus-visible:ring-ring data-selected:bg-primary data-selected:text-primary-foreground data-disabled:pointer-events-none data-disabled:opacity-30 data-outside-month:opacity-30 flex size-9 items-center justify-center rounded-md text-sm outline-none transition-colors focus-visible:ring-2">{date.day}</DatePicker.Day>
                      </DatePicker.Cell>
                    {/each}
                  </DatePicker.GridRow>
                {/each}
              </DatePicker.GridBody>
            </DatePicker.Grid>
          {/each}
        {/snippet}
      </DatePicker.Calendar>
    </DatePicker.Content>
  </DatePicker.Portal>
</DatePicker.Root>
