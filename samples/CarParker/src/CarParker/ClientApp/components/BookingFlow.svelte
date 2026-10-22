<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { CalendarDays, Loader2, Repeat2, ShieldCheck } from '@lucide/svelte'
  import BookingSummary, { type Quote } from '@/components/BookingSummary.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import Step from '@/components/Step.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { money } from '@/lib/money'
  import { coverPremium, quoteWindow, toMinutes } from '@/lib/pricing'
  import { cn } from '@/lib/utils'
  import type { LotCard, SpotView, Tariff } from '@/types'

  /**
   * Booking flow: When, Spot, Cover, Pay as four numbered steps beside a sticky price
   * summary. Single window or recurring weekly; the quote mirrors the server's pricing.
   */
  let {
    lot,
    tariff,
    spots,
    prefillSpot,
  }: {
    lot: LotCard
    tariff: Tariff
    spots: SpotView[]
    prefillSpot: string | null
  } = $props()

  const MAX_WEEKS = 8
  // .NET day-of-week numbers (Sunday = 0); the chips read Monday-first.
  const weekDays = [
    { num: 1, short: 'Mon' }, { num: 2, short: 'Tue' }, { num: 3, short: 'Wed' }, { num: 4, short: 'Thu' },
    { num: 5, short: 'Fri' }, { num: 6, short: 'Sat' }, { num: 0, short: 'Sun' },
  ]
  const tomorrow = () => new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)
  const pad = (n: number) => String(n).padStart(2, '0')

  let selected = $state<SpotView | undefined>(
    (prefillSpot && spots.find(s => s.id === prefillSpot && s.state === 'free')) || undefined,
  )
  // Keep the chosen bay in sync with live flips (someone else may take it).
  const chosen = $derived(selected ? (spots.find(s => s.id === selected!.id) ?? selected) : null)
  const spotTaken = $derived(!!chosen && chosen.state !== 'free')

  let tab = $state<'single' | 'recurring'>('single')
  let singleDate = $state(tomorrow())
  let singleStart = $state('14:00')
  let singleEnd = $state('18:00')
  let days = $state<number[]>([1, 3, 5])
  let timeFrom = $state('08:00')
  let timeTo = $state('18:00')
  let weeksInput = $state('4')
  const weeks = $derived(Math.max(1, Math.min(MAX_WEEKS, Math.round(Number(weeksInput)) || 1)))

  const form = useForm({
    lotId: lot.id,
    spotId: '',
    windowType: 'single',
    startAtUtc: null as string | null,
    endAtUtc: null as string | null,
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

  // Keep the payload in sync with the flow state.
  $effect.pre(() => {
    form.windowType = tab
    form.timeFrom = timeFrom
    form.timeTo = timeTo
    form.weeks = weeks
    form.daysOfWeek = days.join(',')
    form.spotId = chosen?.id ?? ''
    form.startAtUtc = tab === 'single' && singleDate && singleStart ? `${singleDate}T${singleStart}:00Z` : null
    form.endAtUtc = tab === 'single' && singleDate && singleEnd ? `${singleDate}T${singleEnd}:00Z` : null
  })

  const range = $derived(tab === 'single' ? [toMinutes(singleStart), toMinutes(singleEnd)] : [toMinutes(timeFrom), toMinutes(timeTo)])
  const rangeValid = $derived(range[1]! > range[0]!)

  const visits = $derived.by(() => {
    if (!rangeValid) return 0
    if (tab === 'single') return singleDate ? 1 : 0
    if (!days.length) return 0
    let count = 0
    for (let offset = 1; offset <= weeks * 7; offset++) {
      if (days.includes(new Date(Date.now() + offset * 86_400_000).getUTCDay())) count++
    }
    return count
  })

  const quote = $derived.by<Quote>(() => {
    const per = rangeValid ? quoteWindow(tariff, range[0]!, range[1]!) : { hours: 0, cents: 0, uncappedCents: 0, capped: false }
    const parkingCents = per.cents * visits
    const coverCents = form.insurance && visits ? coverPremium(tariff, parkingCents) : 0
    return {
      windows: visits,
      hours: per.hours,
      parkingCents,
      uncappedCents: per.uncappedCents,
      capped: per.capped,
      coverCents,
      totalCents: parkingCents + coverCents,
    }
  })

  const whenSummary = $derived(
    !visits ? '' : tab === 'single' ? `${singleDate} · ${singleStart}–${singleEnd} UTC` : `${visits} visits · ${timeFrom}–${timeTo} UTC`,
  )
  const plateOk = $derived(form.plateNo.trim().length >= 2)

  const disabledReason = $derived(
    !visits ? 'Choose a valid time window.'
      : !chosen ? 'Choose a bay on the map.'
        : spotTaken ? 'That bay was just taken. Choose another.'
          : null,
  )

  const toggleDay = (num: number) => {
    days = days.includes(num) ? days.filter(d => d !== num) : [...days, num].sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7))
  }

  /** Quick durations: set the end time relative to the start (single window only). */
  const durations = [1, 2, 4, 8]
  const setDuration = (hours: number) => {
    const end = toMinutes(singleStart) + hours * 60
    if (end < 24 * 60) singleEnd = `${pad(Math.floor(end / 60))}:${pad(end % 60)}`
  }
  const durationFits = (hours: number) => toMinutes(singleStart) + hours * 60 < 24 * 60
  const currentHours = $derived((toMinutes(singleEnd) - toMinutes(singleStart)) / 60)

  const methodItems = [
    { value: 'card', label: 'Card' },
    { value: 'mobile_money', label: 'Mobile money' },
    { value: 'pay_at_gate', label: 'Pay at the gate' },
  ]

  const submit = () =>
    form.post('/account/bookings', {
      // A taken bay or stale window: fresh attempt id for the retry.
      onError: () => { form.attemptId = crypto.randomUUID() },
    })
</script>

<div class="grid items-start gap-6 lg:grid-cols-[1fr_22rem]">
  <div class="flex min-w-0 flex-col gap-4">
    <!-- 1 · When -->
    <Step number={1} title="When" done={!!visits} summary={whenSummary} id="when">
      <div class="bg-muted grid grid-cols-2 gap-1 rounded-xl p-1" role="tablist" aria-label="Booking type">
        {#each ([['single', 'One-off', CalendarDays], ['recurring', 'Weekly', Repeat2]] as const) as [value, label, Icon] (value)}
          <button
            type="button"
            role="tab"
            aria-selected={tab === value}
            onclick={() => (tab = value)}
            class={cn(
              'flex min-h-11 items-center justify-center gap-2 rounded-lg px-3 text-base font-semibold transition-colors',
              tab === value ? 'bg-card shadow-card' : 'text-muted-foreground hover:text-foreground',
            )}
          ><Icon class="size-4" aria-hidden="true" /> {label}</button>
        {/each}
      </div>

      {#if tab === 'single'}
        <div class="mt-5 grid gap-4 sm:grid-cols-3">
          <div class="grid gap-2 sm:col-span-3 sm:max-w-xs">
            <Label for="single-date">Date</Label>
            <Input id="single-date" type="date" bind:value={singleDate} min={tomorrow()} required />
          </div>
          <div class="grid gap-2">
            <Label for="single-start">Arrive (UTC)</Label>
            <Input id="single-start" type="time" bind:value={singleStart} required />
          </div>
          <div class="grid gap-2">
            <Label for="single-end">Leave (UTC)</Label>
            <Input id="single-end" type="time" bind:value={singleEnd} required />
          </div>
          <div class="grid gap-2">
            <Label id="duration-label">Stay</Label>
            <div class="flex gap-1.5" role="group" aria-labelledby="duration-label">
              {#each durations as hours (hours)}
                <button
                  type="button"
                  aria-pressed={currentHours === hours}
                  disabled={!durationFits(hours)}
                  onclick={() => setDuration(hours)}
                  class={cn(
                    'ring-input min-h-11 flex-1 rounded-lg text-sm font-semibold ring-1 ring-inset transition-colors disabled:opacity-40 md:min-h-10',
                    currentHours === hours ? 'bg-primary text-primary-foreground ring-primary' : 'bg-card hover:bg-accent',
                  )}
                >{hours}h</button>
              {/each}
            </div>
          </div>
        </div>
        {#if !rangeValid}<p class="text-destructive mt-3 text-sm" role="alert">Leave time must be after arrival on the same day.</p>{/if}
      {:else}
        <div class="mt-5 grid gap-4">
          <div class="grid gap-2">
            <Label id="days-label">Days</Label>
            <div class="flex flex-wrap gap-1.5" role="group" aria-labelledby="days-label">
              {#each weekDays as day (day.num)}
                <button
                  type="button"
                  aria-pressed={days.includes(day.num)}
                  onclick={() => toggleDay(day.num)}
                  class={cn(
                    'ring-input min-h-11 min-w-14 rounded-lg px-3 text-sm font-semibold ring-1 ring-inset transition-colors md:min-h-10',
                    days.includes(day.num) ? 'bg-primary text-primary-foreground ring-primary' : 'bg-card hover:bg-accent',
                  )}
                >{day.short}</button>
              {/each}
            </div>
          </div>
          <div class="grid gap-4 sm:grid-cols-3">
            <div class="grid gap-2">
              <Label for="time-from">Arrive (UTC)</Label>
              <Input id="time-from" type="time" bind:value={timeFrom} required />
            </div>
            <div class="grid gap-2">
              <Label for="time-to">Leave (UTC)</Label>
              <Input id="time-to" type="time" bind:value={timeTo} required />
            </div>
            <div class="grid gap-2">
              <Label for="weeks">For how many weeks (max {MAX_WEEKS})</Label>
              <Input id="weeks" type="number" min="1" max={MAX_WEEKS} bind:value={weeksInput} />
            </div>
          </div>
          <p class="text-muted-foreground text-sm">
            {#if visits}{visits} visit{visits === 1 ? '' : 's'}, starting tomorrow, on the same bay each time.{:else}Pick at least one day and a valid time range.{/if}
          </p>
        </div>
      {/if}
      {#if form.errors.startAtUtc}<p class="text-destructive mt-3 text-sm" role="alert">{form.errors.startAtUtc}</p>{/if}
    </Step>

    <!-- 2 · Spot -->
    <Step number={2} title="Choose a bay" done={!!chosen && !spotTaken} summary={chosen ? `${chosen.label} · level ${chosen.level}` : ''} id="spot">
      <SpotGrid {spots} selectable selectedId={chosen?.id} onselect={(spot) => (selected = spot)} />
      {#if !spots.some(s => s.state === 'free')}
        <p class="text-destructive mt-4 text-sm" role="alert">This lot has no free bays right now.</p>
      {/if}
      {#if form.errors.spotId}<p class="text-destructive mt-3 text-sm" role="alert">{form.errors.spotId}</p>{/if}
    </Step>

    <!-- 3 · Cover -->
    <Step number={3} title="Spot Cover" done id="cover" summary={form.insurance ? 'Added' : 'Not added'}>
      <label class="bg-muted flex cursor-pointer items-start gap-4 rounded-xl p-4">
        <Switch id="insurance" bind:checked={form.insurance} class="mt-0.5" aria-label="Add Spot Cover" />
        <span class="min-w-0 flex-1">
          <span class="flex items-center gap-2 font-semibold"><ShieldCheck class="text-primary size-5" aria-hidden="true" /> Cover damage and theft</span>
          <span class="text-muted-foreground mt-1 block text-sm leading-6">
            Pays out up to {money(tariff.coverageCents)} at this lot for {tariff.insurancePercent}% of the parking charge (minimum {money(tariff.insuranceMinCents)}). You can file a claim from your account.
          </span>
        </span>
      </label>
    </Step>

    <!-- 4 · Pay -->
    <Step number={4} title="Vehicle and payment" done={plateOk} summary={plateOk ? form.plateNo.toUpperCase() : ''} id="pay">
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-2">
          <Label for="plate">Number plate</Label>
          <Input id="plate" bind:value={form.plateNo} placeholder="ABX 442" autocomplete="off" maxlength="16" class="font-display text-lg font-bold tracking-wider uppercase" />
          {#if form.errors.plateNo}<p class="text-destructive text-sm" role="alert">{form.errors.plateNo}</p>{/if}
        </div>
        <div class="grid gap-2">
          <Label for="method">Pay by</Label>
          <Select id="method" items={methodItems} bind:value={form.method} ariaLabel="Payment method" class="w-full" />
          {#if form.errors.method}<p class="text-destructive text-sm" role="alert">{form.errors.method}</p>{/if}
        </div>
        <div class="grid gap-2 sm:col-span-2">
          <Label for="note">Note for the guard (optional)</Label>
          <Textarea id="note" bind:value={form.note} maxlength="280" rows={2} />
          {#if form.errors.note}<p class="text-destructive text-sm" role="alert">{form.errors.note}</p>{/if}
        </div>
      </div>
    </Step>
  </div>

  <!-- Summary: sticky beside the steps on desktop -->
  <aside class="lg:sticky lg:top-24" aria-label="Price summary">
    <BookingSummary
      {lot}
      {tariff}
      spot={chosen}
      {quote}
      cover={form.insurance}
      payAtGate={form.method === 'pay_at_gate'}
      processing={form.processing}
      {disabledReason}
      onsubmit={submit}
    />
  </aside>
</div>

<!-- Mobile: total and reserve stay in reach -->
<div class="bg-card fixed inset-x-0 bottom-0 z-30 flex items-center gap-3 px-4 py-3 shadow-pop lg:hidden">
  <div class="min-w-0 flex-1">
    <p class="text-muted-foreground text-xs">Total</p>
    <p class="font-display text-2xl leading-none font-bold tabular-nums">{money(quote.totalCents)}</p>
  </div>
  <Button type="button" size="lg" disabled={form.processing || !!disabledReason} onclick={submit}>
    {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    Reserve
  </Button>
</div>
