<script lang="ts">
  import { useForm, router } from '@inertiajs/svelte'
  import { CarFront, CircleCheck, Loader2, LogIn, LogOut, SearchX } from '@lucide/svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import { fmtTime } from '@/lib/time'
  import { cn } from '@/lib/utils'
  import type { GraceWindow, LotOption, QueueRow, SessionRow, SpotView } from '@/types'

  /**
   * GET /guard: gate console for a tablet. Type or scan a plate and the matching booking or
   * open session answers; otherwise tap the booked bay to check in a different plate. GET /guard/board renders the same
   * component with `boardOnly` for the compact session board.
   */
  let {
    lots,
    lotId,
    spots = [],
    queue = [],
    openSessions,
    grace,
    boardOnly,
    exitedSessions = [],
  }: {
    lots: LotOption[]
    lotId?: string | null
    spots?: SpotView[]
    queue?: QueueRow[]
    openSessions: SessionRow[]
    grace?: GraceWindow
    boardOnly?: boolean
    exitedSessions?: SessionRow[]
  } = $props()

  const form = useForm<{ lotId: string; spotId: string; plateNo: string }>({ lotId: lotId ?? lots[0]?.id ?? '', spotId: '', plateNo: '' })

  // svelte-ignore state_referenced_locally
  const lotName = lots.find(l => l.id === lotId)?.name ?? 'Lot'
  const now = $derived(Date.now())

  const norm = (value: string | null | undefined) => (value ?? '').replace(/[^a-z0-9]/gi, '').toUpperCase()
  const spotByLabel = (label: string | null) => spots.find(s => s.label === label) ?? null

  let manualId = $state<string | null>(null)
  const manual = $derived(manualId ? (spots.find(s => s.id === manualId) ?? null) : null)

  const plate = $derived(norm(form.plateNo))
  const openMatch = $derived(plate ? openSessions.find(s => norm(s.plateNo) === plate) : undefined)
  const queueMatch = $derived(plate && !openMatch ? queue.find(r => norm(r.plateNo) === plate && r.status === 'scheduled') : undefined)
  const matched = $derived(spotByLabel(openMatch?.spotLabel ?? queueMatch?.spotLabel ?? null))
  const target = $derived(manual ?? matched)

  /** Check-in opens 15 min before the window starts (GuardService.GraceMinutes). */
  const ready = (row: QueueRow) => {
    const start = new Date(row.startAtUtc).getTime()
    return start - 15 * 60_000 <= now && new Date(row.endAtUtc).getTime() >= now
  }
  const opensAt = (row: QueueRow) => fmtTime(new Date(new Date(row.startAtUtc).getTime() - 15 * 60_000).toISOString())

  const canCheckIn = $derived(!!target && (target.state === 'free' || target.state === 'booked'))
  const canCheckOut = $derived(target?.state === 'occupied')

  const overdue = (session: SessionRow) => new Date(session.enteredAtUtc).getTime() + (grace?.overstay ?? 20) * 60_000 < now
  const held = (session: SessionRow) => {
    const hours = Math.max(0, ((session.exitedAtUtc ? new Date(session.exitedAtUtc).getTime() : now) - new Date(session.enteredAtUtc).getTime()) / 3_600_000)
    return `${hours.toFixed(1)} h`
  }

  const act = (path: '/guard/check-in' | '/guard/check-out') => {
    if (!target) return
    form.spotId = target.id
    form.lotId = lotId ?? ''
    form.post(path, {
      onSuccess: () => {
        manualId = null
        form.plateNo = ''
      },
    })
  }

  const usePlate = (value: string | null) => {
    manualId = null
    form.plateNo = value ?? ''
  }

  const resolveOverstay = (sessionId: string) => router.post(`/guard/sessions/${sessionId}/resolve`, { note: '' })
</script>

<PageTitle title={boardOnly ? 'Session board' : 'Gate'} />

{#snippet plateBadge(value: string | null)}
  <span class="plate text-base">{value ?? '?'}</span>
{/snippet}

{#if boardOnly}
  <div class="flex flex-col gap-6">
    <h1 class="text-3xl font-bold tracking-tight">Session board</h1>
    <div class="grid gap-6 xl:grid-cols-2">
      <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="open-title">
        <h2 id="open-title" class="px-5 pt-5 pb-3 text-xl font-bold">On site <span class="text-muted-foreground tabular-nums">({openSessions.length})</span></h2>
        {#if openSessions.length}
          <ul class="divide-y">
            {#each openSessions as session (session.id)}
              <li class="flex items-center gap-3 px-5 py-3.5">
                <span class="bg-foreground text-background font-display grid h-9 min-w-12 place-items-center rounded-md px-2 text-lg font-bold">{session.spotLabel ?? '-'}</span>
                {@render plateBadge(session.plateNo)}
                <span class="text-muted-foreground ml-auto text-sm tabular-nums">in {fmtTime(session.enteredAtUtc)} · {held(session)}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="text-muted-foreground px-5 pb-8 text-center">No cars on site.</p>
        {/if}
      </section>
      <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="exited-title">
        <h2 id="exited-title" class="px-5 pt-5 pb-3 text-xl font-bold">Left today</h2>
        {#if exitedSessions.length}
          <ul class="divide-y">
            {#each exitedSessions as session (session.id)}
              <li class="flex items-center gap-3 px-5 py-3.5">
                <span class="bg-muted font-display grid h-9 min-w-12 place-items-center rounded-md px-2 text-lg font-bold">{session.spotLabel ?? '?'}</span>
                {@render plateBadge(session.plateNo)}
                <span class="text-muted-foreground ml-auto text-sm tabular-nums">out {session.exitedAtUtc ? fmtTime(session.exitedAtUtc) : '-'}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="text-muted-foreground px-5 pb-8 text-center">Nobody has left yet today.</p>
        {/if}
      </section>
    </div>
  </div>
{:else if !lots.length}
  <div class="bg-card shadow-card rounded-xl">
    <EmptyState icon={CarFront} title="No lots are open" description="Ask an admin to open a lot for bookings." />
  </div>
{:else}
  <h1 class="sr-only">Gate console, {lotName}</h1>
  <div class="grid items-start gap-6 lg:grid-cols-[minmax(0,25rem)_1fr]">
    <!-- Gate: plate in, answer out -->
    <section class="bg-foreground text-background rounded-xl p-5 lg:sticky lg:top-24" aria-labelledby="gate-title">
      <h2 id="gate-title" class="text-background/80 text-sm font-semibold tracking-wider uppercase">{lotName}</h2>
      <div class="mt-3 grid gap-2">
        <Label for="plate" class="text-background/80 text-base">Number plate</Label>
        <input
          id="plate"
          bind:value={form.plateNo}
          oninput={() => (manualId = null)}
          placeholder="ABX 442"
          maxlength="16"
          autocomplete="off"
          autocapitalize="characters"
          spellcheck="false"
          class="plate placeholder:text-black/30 h-20 w-full text-center text-4xl outline-none focus-visible:ring-4 focus-visible:ring-white/60"
        />
        {#if form.errors.plateNo}<p class="text-cta text-base" role="alert">{form.errors.plateNo}</p>{/if}
      </div>

      <div class="mt-4 min-h-24" aria-live="polite">
        {#if openMatch}
          <div class="bg-spot-free text-spot-free-foreground rounded-lg p-4">
            <p class="flex items-center gap-2 text-lg font-bold"><CircleCheck class="size-6" aria-hidden="true" /> On site, bay {openMatch.spotLabel}</p>
            <p class="mt-0.5 text-base tabular-nums">In since {fmtTime(openMatch.enteredAtUtc)} · {held(openMatch)}</p>
          </div>
        {:else if queueMatch}
          <div class={cn('rounded-lg p-4', ready(queueMatch) ? 'bg-spot-free text-spot-free-foreground' : 'bg-cta text-black')}>
            <p class="flex items-center gap-2 text-lg font-bold"><CircleCheck class="size-6" aria-hidden="true" /> Booked, bay {queueMatch.spotLabel}</p>
            <p class="mt-0.5 text-base tabular-nums">
              {fmtTime(queueMatch.startAtUtc)} to {fmtTime(queueMatch.endAtUtc)} UTC
              {#if !ready(queueMatch)} · check-in opens {opensAt(queueMatch)}{/if}
            </p>
          </div>
        {:else if manual}
          <div class="bg-background/15 rounded-lg p-4">
            <p class="text-lg font-bold">Bay {manual.label} selected</p>
            <p class="text-background/80 text-base capitalize">{manual.state}{manual.type !== 'standard' ? `, ${manual.type}` : ''}</p>
          </div>
        {:else if plate}
          <div class="bg-background/15 rounded-lg p-4">
            <p class="flex items-center gap-2 text-lg font-bold"><SearchX class="size-6" aria-hidden="true" /> No booking for this plate</p>
            <p class="text-background/80 text-base">Check the plate, or tap the bay the driver booked to check in a different car.</p>
          </div>
        {:else}
          <p class="text-background/70 pt-2 text-base">Enter a plate, or tap a bay on the plan.</p>
        {/if}
      </div>

      <div class="mt-4 grid grid-cols-2 gap-3">
        <button
          type="button"
          disabled={form.processing || !canCheckIn}
          onclick={() => act('/guard/check-in')}
          class="bg-spot-free text-spot-free-foreground flex h-20 items-center justify-center gap-2 rounded-xl text-xl font-bold transition-opacity focus-visible:ring-4 focus-visible:ring-white/60 focus-visible:outline-none disabled:opacity-35"
        >
          {#if form.processing}<Loader2 class="size-6 animate-spin" aria-hidden="true" />{:else}<LogIn class="size-6" aria-hidden="true" />{/if} Check in
        </button>
        <button
          type="button"
          disabled={form.processing || !canCheckOut}
          onclick={() => act('/guard/check-out')}
          class="bg-background text-foreground flex h-20 items-center justify-center gap-2 rounded-xl text-xl font-bold transition-opacity focus-visible:ring-4 focus-visible:ring-white/60 focus-visible:outline-none disabled:opacity-35"
        >
          <LogOut class="size-6" aria-hidden="true" /> Check out
        </button>
      </div>
      {#if form.errors.spotId}<p class="text-cta mt-3 text-base" role="alert">{form.errors.spotId}</p>{/if}
      {#if grace}
        <p class="text-background/70 mt-4 text-sm tabular-nums">
          Check-in opens {grace.checkIn} min early · overstay after {grace.overstay} min · no-show after {grace.noShow} min
        </p>
      {/if}
    </section>

    <div class="flex min-w-0 flex-col gap-6">
      <section class="bg-card shadow-card rounded-xl p-4 sm:p-5" aria-labelledby="plan-title">
        <h2 id="plan-title" class="mb-4 text-xl font-bold">Floor plan</h2>
        <SpotGrid {spots} selectable selectableStates={['free', 'booked', 'occupied']} selectedId={manualId ?? matched?.id} onselect={(spot) => (manualId = spot.id)} />
      </section>

      <div class="grid gap-6 xl:grid-cols-2">
        <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="queue-title">
          <h2 id="queue-title" class="px-5 pt-5 pb-3 text-xl font-bold">Expected today</h2>
          {#if queue.length}
            <ul class="divide-y">
              {#each queue as row (row.id)}
                <li>
                  <button type="button" onclick={() => usePlate(row.plateNo)} class="hover:bg-accent/50 flex min-h-14 w-full flex-wrap items-center gap-x-3 gap-y-1 px-5 py-3 text-left transition-colors">
                    {@render plateBadge(row.plateNo)}
                    <span class="font-semibold">{row.spotLabel ?? ''}</span>
                    <span class="text-muted-foreground text-sm tabular-nums">{fmtTime(row.startAtUtc)} to {fmtTime(row.endAtUtc)}</span>
                    <span class="ml-auto">
                      {#if row.status === 'scheduled' && ready(row)}<StatusBadge status="active" label="Check-in open" size="xs" />{:else}<StatusBadge status={row.status} size="xs" />{/if}
                    </span>
                  </button>
                </li>
              {/each}
            </ul>
          {:else}
            <p class="text-muted-foreground px-5 pb-8 text-center">No more bookings today.</p>
          {/if}
        </section>

        <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="site-title">
          <h2 id="site-title" class="px-5 pt-5 pb-3 text-xl font-bold">On site <span class="text-muted-foreground tabular-nums">({openSessions.length})</span></h2>
          {#if openSessions.length}
            <ul class="divide-y">
              {#each openSessions as session (session.id)}
                <li class="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 py-3">
                  <button type="button" onclick={() => usePlate(session.plateNo)} class="flex min-h-11 items-center gap-3 rounded-lg text-left">
                    {@render plateBadge(session.plateNo)}
                    <span class="font-semibold">{session.spotLabel ?? ''}</span>
                  </button>
                  <span class="text-muted-foreground text-sm tabular-nums">in {fmtTime(session.enteredAtUtc)} · {held(session)}</span>
                  <span class="ml-auto">
                    <ConfirmDialog
                      title={`End the session on ${session.spotLabel ?? 'this bay'}?`}
                      description={overdue(session) ? 'This car has stayed past its window. Ending the session frees the bay and records the overstay.' : 'Ends the session and frees the bay for the next booking.'}
                      confirmLabel="End session"
                      destructive={overdue(session)}
                      onConfirm={() => resolveOverstay(session.id)}
                      triggerClass={cn('min-h-11 rounded-lg px-3 text-sm font-semibold', overdue(session) ? 'text-destructive hover:bg-destructive/10' : 'text-muted-foreground hover:bg-accent')}
                    >
                      {#snippet trigger()}{overdue(session) ? 'Resolve overstay' : 'End session'}{/snippet}
                    </ConfirmDialog>
                  </span>
                </li>
              {/each}
            </ul>
          {:else}
            <p class="text-muted-foreground px-5 pb-8 text-center">No cars on site.</p>
          {/if}
        </section>
      </div>
    </div>
  </div>
{/if}
