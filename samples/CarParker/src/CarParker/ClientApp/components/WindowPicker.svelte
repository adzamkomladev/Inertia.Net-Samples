<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { CalendarDays, CarFront, CreditCard, Loader2, Repeat2, ShieldCheck } from '@lucide/svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { money } from '@/lib/money'
  import { cn } from '@/lib/utils'
  import type { LotCard, SpotView, Tariff } from '@/types'

  /**
   * Booking wizard: single window vs recurring weekly tabs, day-of-week chips, UTC time
   * inputs and a live price preview that mirrors the server's PricingRules exactly —
   * ceiling-hour billing with the daily-max cap per 24h slice, plus the Spot Cover
   * premium (12% of parking, $2 minimum).
   */
  let {
    lot,
    tariff,
    selectedSpot,
    class: className,
  }: {
    lot: LotCard
    tariff: Tariff
    selectedSpot: SpotView | null
    class?: string
  } = $props()

  const MAX_WEEKS = 8
  // .NET day-of-week numbers (Sunday = 0) — the chips read Monday-first.
  const weekDays = [
    { num: 1, short: 'Mon' },
    { num: 2, short: 'Tue' },
    { num: 3, short: 'Wed' },
    { num: 4, short: 'Thu' },
    { num: 5, short: 'Fri' },
    { num: 6, short: 'Sat' },
    { num: 0, short: 'Sun' },
  ]

  const tomorrow = () => {
    const date = new Date(Date.now() + 86_400_000)
    return date.toISOString().slice(0, 10)
  }

  let tab = $state<'single' | 'recurring'>('single')
  let singleDate = $state(tomorrow())
  let singleStart = $state('14:00')
  let singleEnd = $state('18:00')
  let days = $state<number[]>([1, 3, 5])
  let timeFrom = $state('08:00')
  let timeTo = $state('18:00')
  let weeksInput = $state('4')

  // Number inputs hand back strings; clamp once so the preview and payload agree.
  const weeks = $derived(Math.max(1, Math.min(MAX_WEEKS, Math.round(Number(weeksInput)) || 1)))

  const form = useForm({
    lotId: lot.id,
    spotId: selectedSpot?.id ?? '',
    windowType: 'single',
    startAtUtc: '',
    endAtUtc: '',
    daysOfWeek: '1,3,5',
    timeFrom: '08:00',
    timeTo: '18:00',
    weeks: 4,
    plateNo: '',
    insurance: true,
    method: 'card',
    attemptId: crypto.randomUUID(),
    note: '',
  })

  // Keep the payload in sync with the wizard state.
  $effect.pre(() => {
    form.windowType = tab
    form.timeFrom = timeFrom
    form.timeTo = timeTo
    form.weeks = weeks
    form.daysOfWeek = days.join(',')
    form.spotId = selectedSpot?.id ?? ''
    if (tab === 'single') {
      form.startAtUtc = singleDate && singleStart ? `${singleDate}T${singleStart}:00Z` : ''
      form.endAtUtc = singleDate && singleEnd ? `${singleDate}T${singleEnd}:00Z` : ''
    } else {
      form.startAtUtc = ''
      form.endAtUtc = ''
    }
  })

  /** Server-mirror of PricingRules.WindowCents: ceil hours, min 1h, daily-max per 24h slice. */
  function windowCents(startMinutes: number, endMinutes: number): number {
    const minutes = Math.ceil(endMinutes - startMinutes)
    let hours = Math.max(1, Math.ceil(minutes / 60))
    let total = 0
    while (hours > 0) {
      const slice = Math.min(24, hours)
      const sliceCents = slice * tariff.ratePerHourCents
      total += sliceCents > tariff.dailyMaxCents && tariff.dailyMaxCents > 0 ? tariff.dailyMaxCents : sliceCents
      hours -= slice
    }
    return total
  }

  function slots(): { startMinutes: number; endMinutes: number }[] {
    if (tab === 'single') {
      if (!singleDate || !singleStart || !singleEnd) return []
      const from = singleStart.split(':').map(Number) as [number, number]
      const to = singleEnd.split(':').map(Number) as [number, number]
      const start = from[0]! * 60 + from[1]!
      const end = to[0]! * 60 + to[1]!
      if (end <= start) return []
      return [{ startMinutes: start, endMinutes: end }]
    }
    const out: { startMinutes: number; endMinutes: number }[] = []
    const from = timeFrom.split(':').map(Number) as [number, number]
    const to = timeTo.split(':').map(Number) as [number, number]
    const start = from[0]! * 60 + from[1]!
    const end = to[0]! * 60 + to[1]!
    if (end <= start || !days.length) return out
    const today = new Date()
    for (let offset = 1; offset <= Math.min(weeks, MAX_WEEKS) * 7; offset++) {
      const day = new Date(today.getTime() + offset * 86_400_000)
      if (!days.includes(day.getUTCDay())) continue
      out.push({ startMinutes: start, endMinutes: end })
    }
    return out
  }

  const preview = $derived.by(() => {
    const list = slots()
    const parking = list.reduce((sum, slot) => sum + windowCents(slot.startMinutes, slot.endMinutes), 0)
    const premium = form.insurance ? Math.max(tariff.insuranceMinCents, Math.round((parking * tariff.insurancePercent) / 100)) : 0
    const capped = list.some(slot => Math.ceil((slot.endMinutes - slot.startMinutes) / 60) * tariff.ratePerHourCents > tariff.dailyMaxCents)
    return { count: list.length, parking, premium, total: parking + premium, capped }
  })

  const duration = $derived.by(() => {
    if (tab === 'single') {
      if (!singleStart || !singleEnd) return ''
      const [fh, fm] = singleStart.split(':').map(Number)
      const [th, tm] = singleEnd.split(':').map(Number)
      const mins = (th! * 60 + tm!) - (fh! * 60 + fm!)
      if (mins <= 0) return ''
      const hours = Math.ceil(mins / 60)
      return hours === 1 ? '1 hr' : `${hours} hrs`
    }
    return `${Math.min(weeks, MAX_WEEKS)} week${weeks === 1 ? '' : 's'}`
  })

  const toggleDay = (num: number) => {
    days = days.includes(num) ? days.filter(d => d !== num) : [...days, num].sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7))
  }

  const methodItems = [
    { value: 'card', label: 'Card' },
    { value: 'mobile_money', label: 'Mobile money' },
    { value: 'pay_at_gate', label: 'Pay at gate' },
  ]

  const submit = () =>
    form.post('/account/bookings', {
      onError: () => {
        // A taken spot or a stale window: fresh attempt id on the next try.
        form.attemptId = crypto.randomUUID()
      },
    })
</script>

<div class={cn('flex flex-col gap-5', className)}>
  <!-- Window tabs -->
  <div class="bg-muted/60 grid grid-cols-2 gap-1 rounded-xl p-1" role="tablist" aria-label="Booking window type">
    {#each ([['single', 'Single window', CalendarDays], ['recurring', 'Recurring weekly', Repeat2]] as const) as [value, label, Icon] (value)}
      <button
        type="button"
        role="tab"
        aria-selected={tab === value}
        onclick={() => (tab = value as 'single' | 'recurring')}
        class={cn(
          'flex items-center justify-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-all duration-200 ease-spring',
          tab === value ? 'bg-card text-foreground shadow-xs' : 'text-muted-foreground hover:text-foreground',
        )}
      >
        <Icon class="size-4" aria-hidden="true" />
        {label}
      </button>
    {/each}
  </div>

  {#if tab === 'single'}
    <div class="grid gap-4 sm:grid-cols-3">
      <div class="grid gap-2 sm:col-span-3">
        <Label for="single-date">Date</Label>
        <Input id="single-date" type="date" bind:value={singleDate} min={tomorrow()} required />
      </div>
      <div class="grid gap-2">
        <Label for="single-start">Start · UTC</Label>
        <Input id="single-start" type="time" bind:value={singleStart} required />
      </div>
      <div class="grid gap-2">
        <Label for="single-end">End · UTC</Label>
        <Input id="single-end" type="time" bind:value={singleEnd} required />
      </div>
      <div class="grid gap-2">
        <Label>Duration</Label>
        <p class="font-mono flex h-9 items-center rounded-lg border px-3 text-sm tabular-nums">{duration || '—'}</p>
      </div>
    </div>
  {:else}
    <div class="grid gap-4">
      <div class="grid gap-2">
        <Label>Days of week</Label>
        <div class="flex flex-wrap gap-1.5" role="group" aria-label="Days of week">
          {#each weekDays as day (day.num)}
            <button
              type="button"
              aria-pressed={days.includes(day.num)}
              onclick={() => toggleDay(day.num)}
              class={cn(
                'min-w-11 rounded-lg border px-2.5 py-1.5 text-[13px] font-medium transition-all duration-200 ease-spring active:scale-95',
                days.includes(day.num)
                  ? 'border-cta bg-cta text-cta-foreground shadow-xs'
                  : 'bg-card text-muted-foreground hover:text-foreground hover:border-input',
              )}
            >
              {day.short}
            </button>
          {/each}
        </div>
      </div>
      <div class="grid gap-4 sm:grid-cols-3">
        <div class="grid gap-2">
          <Label for="time-from">From · UTC</Label>
          <Input id="time-from" type="time" bind:value={timeFrom} required />
        </div>
        <div class="grid gap-2">
          <Label for="time-to">Until · UTC</Label>
          <Input id="time-to" type="time" bind:value={timeTo} required />
        </div>
        <div class="grid gap-2">
          <Label for="weeks">Weeks (max {MAX_WEEKS})</Label>
          <Input id="weeks" type="number" min="1" max={MAX_WEEKS} bind:value={weeksInput} />
        </div>
      </div>
      <p class="text-muted-foreground text-[13px]">
        Repeats every picked day for {weeks} week{weeks === 1 ? '' : 's'}, starting tomorrow.
      </p>
    </div>
  {/if}

  {#if form.errors.spotId}<p class="text-destructive text-sm" role="alert">{form.errors.spotId}</p>{/if}

  <!-- Plate + payment + insurance -->
  <div class="grid gap-4 sm:grid-cols-2">
    <div class="grid gap-2">
      <Label for="plate">Plate number</Label>
      <Input id="plate" bind:value={form.plateNo} placeholder="e.g. ABX 442" autocomplete="off" maxlength="16" />
      {#if form.errors.plateNo}<p class="text-destructive text-sm" role="alert">{form.errors.plateNo}</p>{/if}
    </div>
    <div class="grid gap-2">
      <Label for="method">Payment</Label>
      <Select id="method" items={methodItems} bind:value={form.method} ariaLabel="Payment method" class="w-full" />
      {#if form.errors.method}<p class="text-destructive text-sm" role="alert">{form.errors.method}</p>{/if}
    </div>
  </div>

  <label class="bg-card flex cursor-pointer items-start gap-3 rounded-2xl border p-4 transition-colors hover:border-input">
    <Switch id="insurance" bind:checked={form.insurance} class="mt-0.5" />
    <span class="min-w-0 flex-1">
      <span class="flex items-center gap-1.5 text-sm font-medium">
        <ShieldCheck class="text-spot-booked size-4" aria-hidden="true" />
        Spot Cover
      </span>
      <span class="text-muted-foreground mt-0.5 block text-[13px] leading-5">
        {tariff.insurancePercent}% of parking (min {money(tariff.insuranceMinCents)}). Covers damage or theft up to {money(tariff.coverageCents)} at this lot.
      </span>
    </span>
  </label>

  <div class="grid gap-2">
    <Label for="note">Note for the guard (optional)</Label>
    <Textarea id="note" bind:value={form.note} placeholder="Anything the deck team should know." maxlength="280" rows={2} />
    {#if form.errors.note}<p class="text-destructive text-sm" role="alert">{form.errors.note}</p>{/if}
  </div>

  <!-- Live price preview -->
  <div class="bg-card shadow-card rounded-2xl border p-5">
    <div class="flex items-center justify-between">
      <p class="font-display text-sm font-semibold">Price preview</p>
      {#if selectedSpot}
        <span class="inline-flex items-center gap-1.5 rounded-full bg-spot-free/15 px-2 py-0.5 text-xs font-medium text-spot-free-foreground">
          <CarFront class="size-3.5" aria-hidden="true" /> Spot {selectedSpot.label}
        </span>
      {:else}
        <span class="text-muted-foreground text-xs">Pick a spot on the map</span>
      {/if}
    </div>
    <dl class="mt-4 space-y-2 text-sm">
      <div class="flex items-baseline justify-between gap-3">
        <dt class="text-muted-foreground">Parking{preview.count ? ` · ${preview.count} window${preview.count === 1 ? '' : 's'}` : ''}</dt>
        <dd><PriceTag cents={preview.parking} /></dd>
      </div>
      {#if form.insurance}
        <div class="flex items-baseline justify-between gap-3">
          <dt class="text-muted-foreground">Spot Cover premium</dt>
          <dd><PriceTag cents={preview.premium} /></dd>
        </div>
      {/if}
      <div class="border-t pt-2.5">
        <div class="flex items-baseline justify-between gap-3">
          <dt class="font-medium">Total</dt>
          <dd><PriceTag cents={preview.total} size="xl" /></dd>
        </div>
        {#if preview.capped}
          <p class="text-muted-foreground mt-1 text-right text-[11px]">daily max {money(tariff.dailyMaxCents)} applied</p>
        {/if}
      </div>
    </dl>

    <Button
      type="button"
      size="lg"
      class="mt-5 w-full"
      disabled={form.processing || !selectedSpot || !preview.count}
      onclick={submit}
    >
      {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
      {form.processing ? 'Reserving…' : `Reserve for ${money(preview.total)}`}
    </Button>
    <p class="text-muted-foreground mt-3 flex items-center justify-center gap-1.5 text-center text-[11px]">
      <CreditCard class="size-3" aria-hidden="true" />
      {form.method === 'pay_at_gate' ? 'Pay when you roll in' : 'Charged at booking'} · cancel up to 24h ahead for a full refund
    </p>
  </div>
</div>
