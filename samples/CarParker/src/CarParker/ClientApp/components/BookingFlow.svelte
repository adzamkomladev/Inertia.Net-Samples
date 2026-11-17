<script lang="ts">
  import { untrack } from 'svelte'
  import { router, useForm, Link } from '@inertiajs/svelte'
  import CalendarDays from '@lucide/svelte/icons/calendar-days'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import Repeat2 from '@lucide/svelte/icons/repeat-2'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import BookingSummary, { refundRule, type Quote } from '@/components/BookingSummary.svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import Step from '@/components/Step.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import DatePicker from '@/components/ui/date-picker/DatePicker.svelte'
  import TimePicker from '@/components/ui/time-picker/TimePicker.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Switch from '@/components/ui/switch/Switch.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { money } from '@/lib/money'
  import { coverPremium, quoteWindow, toMinutes } from '@/lib/pricing'
  import { addDays, fmtWindow, weekdayOf, zoneLabel, zonedToday, zonedToUtc } from '@/lib/time'
  import { cn } from '@/lib/utils'
  import type { LotCard, SpotView, Tariff, Vehicle } from '@/types'
  import Check from '@lucide/svelte/icons/check'
  import Info from '@lucide/svelte/icons/info'

  /**
   * Booking flow: When, Spot, Cover, Pay as four numbered steps beside a sticky price
   * summary. Single window or recurring weekly; the quote mirrors the server's pricing.
   * Every time is entered and shown on the lot's clock; the server converts to UTC. Once the
   * window is valid the bay map is reloaded (partial, debounced) to show what is free then.
   */
  let {
    lot,
    tariff,
    spots,
    spotsWindow = null,
    prefillSpot,
    vehicles = [],
  }: {
    lot: LotCard
    tariff: Tariff
    spots: SpotView[]
    /** Echo of the window the bay map reflects; null while it shows bays free right now. */
    spotsWindow?: string | null
    prefillSpot: string | null
    vehicles?: Vehicle[]
  } = $props()

  const MAX_WEEKS = 8
  // .NET day-of-week numbers (Sunday = 0); the chips read Monday-first.
  const weekDays = [
    { num: 1, short: 'Mon' }, { num: 2, short: 'Tue' }, { num: 3, short: 'Wed' }, { num: 4, short: 'Thu' },
    { num: 5, short: 'Fri' }, { num: 6, short: 'Sat' }, { num: 0, short: 'Sun' },
  ]
  const tz = $derived(lot.timeZoneId)
  // The lot's calendar, not the device's: a driver planning from another zone still books lot-local days.
  const today = untrack(() => zonedToday(lot.timeZoneId))
  const tomorrow = addDays(today, 1)
  const pad = (n: number) => String(n).padStart(2, '0')

  let selected = $state<SpotView | undefined>(
    untrack(() => (prefillSpot && spots.find(s => s.id === prefillSpot && s.state === 'free')) || undefined),
  )
  // Keep the chosen bay in sync with live flips (someone else may take it).
  const chosen = $derived(selected ? (spots.find(s => s.id === selected!.id) ?? selected) : null)
  const spotTaken = $derived(!!chosen && chosen.state !== 'free')
  /** Said once when a window change takes the chosen bay away. */
  let bayNotice = $state<string | null>(null)

  let tab = $state<'single' | 'recurring'>('single')
  let singleDate = $state(tomorrow)
  let singleStart = $state('14:00')
  let singleEnd = $state('18:00')
  let days = $state<number[]>([1, 3, 5])
  let timeFrom = $state('08:00')
  let timeTo = $state('18:00')
  let weeksInput = $state('4')
  const weeks = $derived(Math.max(1, Math.min(MAX_WEEKS, Math.round(Number(weeksInput)) || 1)))

  const form = useForm(untrack(() => ({
    lotId: lot.id,
    spotId: '',
    windowType: 'single',
    startAtUtc: null as string | null,
    endAtUtc: null as string | null,
    daysOfWeek: '1,3,5',
    timeFrom: '08:00',
    timeTo: '18:00',
    weeks: 4,
    plateNo: vehicles.find(v => v.isDefault)?.plate ?? '',
    insurance: false,
    method: 'card',
    attemptId: crypto.randomUUID(),
    note: '',
  })))

  const range = $derived(tab === 'single' ? [toMinutes(singleStart), toMinutes(singleEnd)] : [toMinutes(timeFrom), toMinutes(timeTo)])
  const rangeValid = $derived(range[1]! > range[0]!)

  // Lot-local days of the weekly pattern, from tomorrow, as the server materializes them.
  const weeklyDates = $derived.by(() => {
    const list: string[] = []
    for (let offset = 1; offset <= weeks * 7; offset++) {
      const date = addDays(today, offset)
      if (days.includes(weekdayOf(date))) list.push(date)
    }
    return list
  })

  const visits = $derived.by(() => {
    if (!rangeValid) return 0
    if (tab === 'single') return singleDate ? 1 : 0
    return days.length ? weeklyDates.length : 0
  })

  /** Every window of the booking as instants, one per visit, converted on the lot's clock like the server. */
  const windows = $derived.by(() => {
    if (!visits) return []
    const dates = tab === 'single' ? [singleDate] : weeklyDates
    const [from, to] = tab === 'single' ? [singleStart, singleEnd] : [timeFrom, timeTo]
    return dates.map(date => ({ start: zonedToUtc(date, from, tz), end: zonedToUtc(date, to, tz) }))
  })
  /** The first window's start and end as instants (null until the window is valid). */
  const firstWindow = $derived(windows[0] ?? null)

  /** Opening hours are checked as the server does (LotHours.Contains), so the When step can say why a window is refused. */
  const hoursError = $derived(
    lot.open24Hours || !rangeValid || (range[0]! >= toMinutes(lot.opens) && range[1]! <= toMinutes(lot.closes)) ? null
      : `This lot is open ${lot.opens}–${lot.closes} (${zoneLabel(tz)}). Choose arrive and leave times inside those hours.`,
  )

  const dayList = (nums: number[]) => {
    const names = weekDays.filter(d => nums.includes(d.num)).map(d => d.short)
    return names.length > 1 ? `${names.slice(0, -1).join(', ')} & ${names.at(-1)}` : (names[0] ?? '')
  }

  /** "Wed 18 Nov, 14:00–18:00" or "Mon, Wed & Fri, 08:00–18:00 · 4 weeks", on the lot's clock. */
  const whenLabel = $derived(
    !firstWindow ? ''
      : tab === 'single' ? fmtWindow(firstWindow.start.toISOString(), firstWindow.end.toISOString(), tz)
      : `${dayList(days)}, ${timeFrom}–${timeTo} · ${weeks} week${weeks === 1 ? '' : 's'}`,
  )
  const zoneNote = $derived(zoneLabel(tz, firstWindow?.start ?? new Date(`${tomorrow}T12:00:00Z`)))

  // ---- bay map for the chosen window: debounced partial reload of `spots` ----
  const windowQuery = $derived<Record<string, string | number> | null>(
    !visits || hoursError ? null
      : tab === 'single' ? { windowType: 'single', date: singleDate, timeFrom: singleStart, timeTo: singleEnd }
      : { windowType: 'recurring', timeFrom, timeTo, daysOfWeek: days.join(','), weeks },
  )
  // Mirrors the server's echo (Lots.BookPageAsync) so the map knows which window it shows.
  const windowKey = $derived(windowQuery ? Object.values(windowQuery).join('|') : null)
  const initialWindowKey = untrack(() => windowKey)
  // The When step only counts as done once the traveller has chosen something (or pressed Reserve).
  let whenTouched = $state(false)
  /** The window key whose reload last finished, whatever the answer: the server sends no echo for a window it cannot check. */
  let checkedKey = $state<string | null>(null)
  const mapForWindow = $derived(!!windowKey && spotsWindow === windowKey)
  const mapPending = $derived(!!windowKey && spotsWindow !== windowKey && checkedKey !== windowKey)
  /** The reload finished but the server did not answer for this window (invalid or outside opening hours). */
  const mapUnchecked = $derived(!!windowKey && spotsWindow !== windowKey && checkedKey === windowKey)

  $effect(() => {
    const key = windowKey
    const query = windowQuery
    if (key !== initialWindowKey) whenTouched = true
    if (!key || !query || key === spotsWindow) return
    const timer = setTimeout(() => {
      router.reload({
        only: ['spots', 'spotsWindow'],
        data: query,
        replace: true,
        onSuccess: page => {
          const fresh = (page.props as { spots?: SpotView[] }).spots ?? []
          const mine = selected && fresh.find(s => s.id === selected!.id)
          if (mine && mine.state !== 'free') {
            bayNotice = `Bay ${mine.label} is taken for that time, so we cleared it. Choose another bay.`
            selected = undefined
          }
        },
        onFinish: () => { checkedKey = key },
      })
      // The default window loads at once (no typing to wait for); later changes are debounced.
    }, key === initialWindowKey ? 0 : 250)
    return () => clearTimeout(timer)
  })

  const quote = $derived.by<Quote>(() => {
    // The server bills each occurrence by its real elapsed UTC time (a daylight-saving change makes a 00:00 to 03:00
    // window 2 or 4 hours long) and caps each occurrence's days itself, so price every window and sum.
    const per = windows.map(w => quoteWindow(tariff, 0, Math.max(0, Math.floor((w.end.getTime() - w.start.getTime()) / 60_000))))
    const parkingCents = per.reduce((sum, p) => sum + p.cents, 0)
    const uncappedCents = per.reduce((sum, p) => sum + p.uncappedCents, 0)
    const coverCents = form.insurance && visits ? coverPremium(tariff, parkingCents) : 0
    return {
      windows: visits,
      hours: per[0]?.hours ?? 0,
      parkingCents,
      uncappedCents,
      capped: parkingCents < uncappedCents,
      coverCents,
      totalCents: parkingCents + coverCents,
    }
  })

  /** What Spot Cover would add to this booking, shown on the switch before it is turned on. */
  const coverPrice = $derived(coverPremium(tariff, quote.parkingCents))
  let coverTouched = $state(false)
  const refund = $derived(refundRule(firstWindow?.start ?? null, tz, form.method === 'pay_at_gate'))
  const plateOk = $derived(form.plateNo.trim().length >= 2)
  const windowError = $derived((form.errors as Record<string, string | undefined>).window)

  // Validation runs when the traveller presses Reserve; the first problem gets the focus, the rest show inline.
  let attempted = $state(false)
  const problems = $derived.by(() => {
    const list: { key: 'when' | 'spot' | 'plate'; target: string; message: string }[] = []
    if (!visits) list.push({ key: 'when', target: tab === 'single' ? 'single-date' : 'time-from', message: tab === 'single' ? 'Choose a date and a leave time after your arrival.' : 'Pick at least one day and a leave time after your arrival.' })
    else if (hoursError) list.push({ key: 'when', target: tab === 'single' ? 'single-start' : 'time-from', message: hoursError })
    if (!chosen) list.push({ key: 'spot', target: 'spot', message: 'Choose a bay on the map.' })
    else if (spotTaken) list.push({ key: 'spot', target: 'spot', message: 'That bay was just taken. Choose another.' })
    if (!plateOk) list.push({ key: 'plate', target: 'plate', message: 'Enter your number plate (at least 2 characters).' })
    return list
  })
  const problem = (key: 'when' | 'spot' | 'plate') => (attempted ? problems.find(p => p.key === key)?.message : undefined)
  const disabledReason = $derived(attempted ? (problems[0]?.message ?? null) : null)
  const whenDone = $derived(!!visits && !hoursError && (whenTouched || attempted) && !problem('when'))
  const coverDone = $derived(coverTouched || attempted)

  const focusTarget = (id: string) => {
    const el = document.getElementById(id)
    if (!el) return
    el.scrollIntoView({ block: 'center', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' })
    const focusable = el.matches('input, button, textarea, [tabindex]') ? el : el.querySelector<HTMLElement>('input, button:not([disabled]), [tabindex="0"]')
    focusable?.focus({ preventScroll: true })
  }

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

  const submit = () => {
    attempted = true
    const first = problems[0]
    if (first) { focusTarget(first.target); return }
    send()
  }

  const send = () =>
    form.transform(data => ({
      ...data,
      // Lot-local fields; the server converts them to UTC on the lot's clock.
      windowType: tab,
      date: tab === 'single' ? singleDate : null,
      timeFrom: tab === 'single' ? singleStart : timeFrom,
      timeTo: tab === 'single' ? singleEnd : timeTo,
      weeks,
      daysOfWeek: days.join(','),
      spotId: chosen?.id ?? '',
      startAtUtc: null,
      endAtUtc: null,
    })).post('/account/bookings', {
      // A taken bay or stale window: fresh attempt id for the retry.
      onError: (errors) => {
        form.attemptId = crypto.randomUUID()
        const windowError = (errors as Record<string, string | undefined>).window
        const field = windowError ? (tab === 'single' ? 'single-date' : 'time-from') : errors.spotId ? 'spot' : errors.plateNo ? 'plate' : errors.method ? 'method' : errors.note ? 'note' : null
        if (field) focusTarget(field)
      },
    })
</script>

<div class="grid items-start gap-6 lg:grid-cols-[1fr_22rem]">
  <div class="flex min-w-0 flex-col gap-4">
    {#if lot.arrivalNotes}
      <aside class="bg-primary/5 ring-primary/15 flex gap-3 rounded-xl p-4 text-sm ring-1 ring-inset" aria-label="Arrival notes">
        <Info class="text-primary mt-0.5 size-5 shrink-0" aria-hidden="true" />
        <p><span class="font-semibold">Before you arrive</span> · <span class="text-muted-foreground">{lot.arrivalNotes}</span></p>
      </aside>
    {/if}
    <!-- 1 · When -->
    <Step number={1} title="When" done={whenDone} summary={whenDone ? whenLabel : ''} id="when">
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
      <p class="text-muted-foreground mt-3 text-sm">Times are {zoneNote}, the lot's local time.</p>

      {#if tab === 'single'}
        <div class="mt-5 grid gap-4 sm:grid-cols-3">
          <div class="grid gap-2 sm:col-span-3 sm:max-w-xs">
            <Label for="single-date">Date</Label>
            <DatePicker label="Date" id="single-date" bind:value={singleDate} min={tomorrow} required />
          </div>
          <div class="grid gap-2">
            <Label for="single-start">Arrive</Label>
            <TimePicker label="Arrive" id="single-start" bind:value={singleStart} required />
          </div>
          <div class="grid gap-2">
            <Label for="single-end">Leave</Label>
            <TimePicker label="Leave" id="single-end" bind:value={singleEnd} required />
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
              <Label for="time-from">Arrive</Label>
              <TimePicker label="Arrive" id="time-from" bind:value={timeFrom} required />
            </div>
            <div class="grid gap-2">
              <Label for="time-to">Leave</Label>
              <TimePicker label="Leave" id="time-to" bind:value={timeTo} required />
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
      {#if windowError}<p class="text-destructive mt-3 text-sm" role="alert">{windowError}</p>{:else if hoursError}<p class="text-destructive mt-3 text-sm" role="alert">{hoursError}</p>{:else if problem('when')}<p class="text-destructive mt-3 text-sm" role="alert">{problem('when')}</p>{/if}
    </Step>

    <!-- 2 · Spot -->
    <Step number={2} title="Choose a bay" done={!!chosen && !spotTaken} summary={chosen ? `${chosen.label} · level ${chosen.level}` : ''} id="spot-step">
      <p class="mb-4 flex min-h-6 items-center gap-2 text-sm" aria-live="polite">
        {#if mapForWindow}
          <span>Bays free <span class="font-semibold tabular-nums">{whenLabel}</span></span>
        {:else if mapPending}
          <Loader2 class="text-muted-foreground size-4 shrink-0 animate-spin" aria-hidden="true" />
          <span class="text-muted-foreground">Checking bays for <span class="text-foreground font-semibold tabular-nums">{whenLabel}</span>…</span>
        {:else if mapUnchecked}
          <span class="text-destructive">We could not check bays for that time. <a href="#when" class="underline underline-offset-4">Fix the time in step 1</a>; the map shows bays free right now.</span>
        {:else if hoursError}
          <span class="text-destructive">That time is outside opening hours. <a href="#when" class="underline underline-offset-4">Fix the time in step 1</a> to see bays free then; the map shows bays free right now.</span>
        {:else}
          <span class="text-muted-foreground">Showing bays free right now. Choose a time to see what is free then.</span>
        {/if}
      </p>
      <!-- While the map still shows another window, dim it so its states never read as the answer for this one. -->
      <div id="spot" tabindex="-1" aria-busy={mapPending} class={cn('outline-none transition-opacity duration-200', mapPending && 'pointer-events-none opacity-50')}>
        <SpotGrid
          {spots}
          selectable
          selectedId={chosen?.id}
          stateLabels={mapForWindow ? { booked: 'Taken then' } : undefined}
          onselect={(spot) => { selected = spot; bayNotice = null }}
        />
      </div>
      {#if bayNotice}<p class="text-destructive mt-4 text-sm" role="status">{bayNotice}</p>{/if}
      {#if !spots.some(s => s.state === 'free')}
        <p class="text-destructive mt-4 text-sm" role="alert">{mapForWindow ? 'No bay is free for that time. Try another time or day.' : 'This lot has no free bays right now.'}</p>
      {/if}
      {#if form.errors.spotId}<p class="text-destructive mt-3 text-sm" role="alert">{form.errors.spotId}</p>{:else if problem('spot')}<p class="text-destructive mt-3 text-sm" role="alert">{problem('spot')}</p>{/if}
    </Step>

    <!-- 3 · Cover -->
    <Step number={3} title="Spot Cover" done={coverDone} id="cover" summary={coverDone ? (form.insurance ? `Added · ${money(coverPrice)}` : 'Not added') : ''}>
      <label class="bg-muted flex cursor-pointer items-start gap-4 rounded-xl p-4">
        <Switch id="insurance" bind:checked={form.insurance} onCheckedChange={() => (coverTouched = true)} class="mt-0.5" aria-label={`Add Spot Cover for ${money(coverPrice)}`} />
        <span class="min-w-0 flex-1">
          <span class="flex flex-wrap items-center gap-x-2 gap-y-1 font-semibold">
            <ShieldCheck class="text-primary size-5" aria-hidden="true" /> Cover damage and theft
            <span class="text-primary ml-auto tabular-nums">+{money(coverPrice)}</span>
          </span>
          <span class="text-muted-foreground mt-1 block text-sm leading-6">
            Pays out up to {money(tariff.coverageCents)} at this lot for {tariff.insurancePercent}% of the parking charge (minimum {money(tariff.insuranceMinCents)}). You can file a claim from your account.
          </span>
        </span>
      </label>
    </Step>

    <!-- 4 · Pay -->
    <Step number={4} title="Vehicle and payment" done={plateOk} summary={plateOk ? form.plateNo.toUpperCase() : ''} id="pay">
      <div class="grid items-start gap-4 sm:grid-cols-2">
          {#if vehicles.length}
            <div class="flex flex-wrap gap-2 sm:col-span-2" role="group" aria-label="Saved vehicles">
              {#each vehicles as vehicle (vehicle.plate)}
                {@const picked = form.plateNo.trim().toUpperCase() === vehicle.plate}
                <button
                  type="button"
                  aria-pressed={picked}
                  onclick={() => (form.plateNo = vehicle.plate)}
                  class={cn('inline-flex min-h-11 items-center gap-2 rounded-lg px-3 text-sm ring-1 md:min-h-10 ring-inset transition-[background-color,box-shadow,transform] active:scale-[0.97]', picked ? 'bg-primary/10 ring-primary ring-2' : 'ring-input hover:bg-accent')}
                >
                  <span class="plate text-xs">{vehicle.plate}</span>
                  {#if vehicle.label}<span class="text-muted-foreground max-w-28 truncate">{vehicle.label}</span>{/if}
                  {#if picked}<Check class="text-primary animate-pop size-4" aria-hidden="true" />{/if}
                </button>
              {/each}
            </div>
          {/if}
        <div class="grid gap-2">
          <Label for="plate">Number plate</Label>
          <Input id="plate" bind:value={form.plateNo} placeholder="ABX 442" autocomplete="off" autocapitalize="characters" spellcheck="false" enterkeyhint="done" maxlength="16" aria-invalid={!!problem('plate') || !!form.errors.plateNo} class="font-display text-lg font-bold tracking-wider uppercase" />
          {#if !vehicles.length}<Link href="/account/preferences" class="text-muted-foreground hover:text-foreground w-fit text-xs underline-offset-4 hover:underline">Save your plates in your profile for faster checkout</Link>{/if}
          {#if form.errors.plateNo}<p class="text-destructive text-sm" role="alert">{form.errors.plateNo}</p>{:else if problem('plate')}<p class="text-destructive text-sm" role="alert">{problem('plate')}</p>{/if}
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
      when={whenLabel}
      refund={refund.line}
      onsubmit={submit}
    />
  </aside>
</div>

<!-- Mobile: total and reserve stay in reach -->
<div class="bg-card/95 pb-safe fixed inset-x-0 bottom-0 z-30 shadow-pop backdrop-blur lg:hidden">
 <div class="mx-auto flex max-w-6xl items-center gap-3 px-4 py-3">
  <div class="min-w-0 flex-1" aria-live="polite">
    <p class="text-muted-foreground truncate text-xs">{quote.windows ? `${quote.hours} h${quote.windows > 1 ? ` × ${quote.windows} visits` : ''}${form.insurance ? ' · with Spot Cover' : ''}` : 'Total'}</p>
    <p class="font-display text-2xl leading-none font-bold tabular-nums">{money(quote.totalCents)}</p>
    <p class="text-muted-foreground mt-1 truncate text-xs">{refund.short}</p>
  </div>
  <Button type="button" size="lg" class="min-h-12 min-w-32" disabled={form.processing} onclick={submit}>
    {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
    Reserve
  </Button>
 </div>
 {#if disabledReason}<p class="text-destructive px-4 pb-2 text-center text-xs" role="alert">{disabledReason}</p>{/if}
</div>
