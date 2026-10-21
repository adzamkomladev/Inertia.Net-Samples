<script lang="ts">
  import { useForm, router } from '@inertiajs/svelte'
  import { CalendarClock, CarFront, CheckCircle2, Loader2, LogIn, LogOut, ShieldAlert, TimerReset } from '@lucide/svelte'
  import SpotGrid from '@/components/SpotGrid.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import { fmtDateTime, fmtTime, fmtWindow } from '@/lib/time'
  import type { GraceWindow, LotOption, QueueRow, SessionRow, SpotView } from '@/types'

  /**
   * GET /guard — scanner console: live grid, today's scheduled queue, big check-in/out
   * actions, open sessions with overstay resolution. GET /guard/board renders this same
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

  let selected = $state<SpotView | null>(null)
  const pick = (spot: SpotView) => (selected = spot)

  // Spot flips from SignalR should keep the selection object in sync.
  const selectedState = $derived(selected ? (spots.find(s => s.id === selected!.id) ?? selected) : null)

  const actionForm = useForm<{ lotId: string; spotId: string; plateNo: string }>({
    lotId: lotId ?? lots[0]?.id ?? '',
    spotId: '',
    plateNo: '',
  })

  // svelte-ignore state_referenced_locally
  const lotName = lots.find(l => l.id === lotId)?.name ?? 'Deck'

  const now = $derived(Date.now())

  /** Check-in opens 15 min before the window starts (GuardService.GraceMinutes). */
  const checkInReady = (row: QueueRow) => {
    const start = new Date(row.startAtUtc).getTime()
    const end = new Date(row.endAtUtc).getTime()
    return row.status === 'scheduled' && start - 15 * 60_000 <= now && end >= now
  }

  /** Sessions past their window end + overstay grace (GuardService.OverstayGraceMinutes). */
  const overdue = (session: { enteredAtUtc: string; endedAt?: string | null }) => {
    if (session.endedAt) return false
    const endCushion = new Date(session.enteredAtUtc).getTime() + (grace?.overstay ?? 20) * 60_000
    return endCushion < now
  }

  const checkIn = () => {
    if (!selectedState) return
    actionForm.spotId = selectedState.id
    actionForm.lotId = lotId ?? ''
    actionForm.post('/guard/check-in', {
      onSuccess: () => {
        selected = null
        actionForm.plateNo = ''
      },
    })
  }
  const checkOut = () => {
    if (!selectedState) return
    actionForm.spotId = selectedState.id
    actionForm.lotId = lotId ?? ''
    actionForm.post('/guard/check-out', {
      onSuccess: () => {
        selected = null
        actionForm.plateNo = ''
      },
    })
  }

  const resolveForm = useForm<{ note: string }>({ note: '' })
  const resolveOverstay = (sessionId: string) =>
    router.post(`/guard/sessions/${sessionId}/resolve`, { note: resolveForm.note })

  const sessionRow = (session: SessionRow) => {
    const hours = Math.max(0, ((session.exitedAtUtc ? new Date(session.exitedAtUtc).getTime() : now) - new Date(session.enteredAtUtc).getTime()) / 3_600_000)
    return `${hours.toFixed(1)}h`
  }
</script>

<PageTitle title={boardOnly ? 'Session board' : 'Guard console'} />

{#if boardOnly}
  <!-- Compact board -->
  <div class="flex flex-col gap-6">
    <PageHeader title="Session board" description="All decks — open first, then today's exits.">
      {#snippet actions()}
        <Button href="/guard" variant="outline" size="sm" class="rounded-2xl">Back to console</Button>
      {/snippet}
    </PageHeader>

    <div class="grid gap-6 xl:grid-cols-2">
      <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
        <h2 class="font-display px-5 pt-5 pb-3 text-base font-semibold">Open sessions <span class="text-muted-foreground font-mono text-sm tabular-nums">({openSessions.length})</span></h2>
        {#if openSessions.length}
          <ul class="divide-y border-t">
            {#each openSessions as session (session.id)}
              <li class="hover:bg-accent/40 flex items-center gap-3 px-5 py-3 text-sm transition-colors">
                <CarFront class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
                <span class="font-mono text-[13px] font-semibold tabular-nums">{session.spotLabel ?? '—'}</span>
                <span class="font-mono tracking-wide tabular-nums">{session.plateNo}</span>
                <span class="text-muted-foreground ml-auto font-mono text-xs tabular-nums">in {fmtTime(session.enteredAtUtc)} · {sessionRow(session)}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="text-muted-foreground border-t px-5 py-10 text-center text-sm">No open sessions right now.</p>
        {/if}
      </section>

      <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
        <h2 class="font-display px-5 pt-5 pb-3 text-base font-semibold">Exited today</h2>
        {#if exitedSessions.length}
          <ul class="divide-y border-t">
            {#each exitedSessions as session (session.id)}
              <li class="hover:bg-accent/40 flex items-center gap-3 px-5 py-3 text-sm transition-colors">
                <CheckCircle2 class="text-spot-free-foreground size-4 shrink-0" aria-hidden="true" />
                <span class="font-mono text-[13px] font-semibold tabular-nums">{session.spotLabel ?? '—'}</span>
                <span class="font-mono tracking-wide tabular-nums">{session.plateNo}</span>
                {#if session.cancelReason}<span class="text-muted-foreground hidden text-xs sm:block">{session.cancelReason}</span>{/if}
                <span class="text-muted-foreground ml-auto font-mono text-xs tabular-nums">out {session.exitedAtUtc ? fmtTime(session.exitedAtUtc) : '—'}</span>
              </li>
            {/each}
          </ul>
        {:else}
          <p class="text-muted-foreground border-t px-5 py-10 text-center text-sm">No exits recorded today.</p>
        {/if}
      </section>
    </div>
  </div>
{:else}
  <div class="flex flex-col gap-6">
    <PageHeader title={lotName} description="Scan, check in and check out — the grid flips live for everyone watching.">
      {#snippet actions()}
        <Button href="/guard/board" variant="outline" size="sm" class="rounded-2xl">Session board</Button>
      {/snippet}
    </PageHeader>

    {#if !lots.length}
      <div class="bg-card shadow-card rounded-3xl border p-16 text-center">
        <p class="font-display font-semibold">No decks are open</p>
        <p class="text-muted-foreground mt-1 text-sm">Ask an admin to open a lot first.</p>
      </div>
    {:else}
      <div class="grid gap-6 xl:grid-cols-[1fr_22rem]">
        <!-- Grid + queue -->
        <div class="flex flex-col gap-6">
          <section class="bg-card shadow-card rounded-3xl border p-5 sm:p-6">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <h2 class="font-display flex items-center gap-2 text-lg font-semibold">
                <ScanLine class="size-5" aria-hidden="true" /> Live grid
              </h2>
              {#if selectedState}
                <span class="bg-spot-free/15 text-spot-free-foreground inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium">
                  <CarFront class="size-3.5" aria-hidden="true" /> {selectedState.label} selected
                </span>
              {:else}
                <span class="text-muted-foreground text-xs">Pick a spot, then check in or out</span>
              {/if}
            </div>
            <SpotGrid {spots} selectable selectedId={selectedState?.id} onselect={pick} compact class="mt-5" />
          </section>

          <!-- Today's queue -->
          <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
            <h2 class="font-display flex items-center gap-2 px-5 pt-5 pb-3 text-base font-semibold">
              <CalendarClock class="size-4" aria-hidden="true" /> Today's windows
            </h2>
            {#if queue.length}
              <ul class="divide-y border-t">
                {#each queue as row (row.id)}
                  <li class={cn('flex flex-wrap items-center gap-x-4 gap-y-1 px-5 py-3 text-sm transition-colors', checkInReady(row) && 'bg-spot-free/[0.06]')}>
                    <span class="font-mono text-[13px] font-semibold tabular-nums">{row.spotLabel ?? '—'}</span>
                    <span class="font-mono tracking-wide tabular-nums">{row.plateNo ?? '?'}</span>
                    <span class="text-muted-foreground font-mono text-xs tabular-nums">{fmtWindow(row.startAtUtc, row.endAtUtc)}</span>
                    {#if checkInReady(row)}
                      <span class="bg-spot-free text-spot-free-foreground ml-auto inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium">
                        <TimerReset class="size-3" aria-hidden="true" /> check-in open
                      </span>
                    {:else}
                      <span class="ml-auto"><StatusBadge status={row.status} size="xs" /></span>
                    {/if}
                  </li>
                {/each}
              </ul>
            {:else}
              <p class="text-muted-foreground border-t px-5 py-10 text-center text-sm">No windows scheduled for the rest of today.</p>
            {/if}
          </section>
        </div>

        <!-- Action rail -->
        <aside class="flex h-fit flex-col gap-4 xl:sticky xl:top-24">
          <section class="bg-primary text-primary-foreground shadow-lifted relative overflow-hidden rounded-3xl p-5">
            <div class="deck-grid absolute inset-0 opacity-30" aria-hidden="true"></div>
            <div class="relative">
              <h2 class="font-display text-base font-semibold">Gate actions</h2>
              <div class="mt-4 grid gap-3">
                <div class="grid gap-1.5">
                  <Label for="plate" class="text-primary-foreground/70 text-xs">Plate on the car</Label>
                  <Input
                    id="plate"
                    bind:value={actionForm.plateNo}
                    placeholder="ABX 442"
                    maxlength="16"
                    class="border-primary-foreground/25 bg-primary-foreground/10 placeholder:text-primary-foreground/40 text-primary-foreground h-11 rounded-xl"
                  />
                  {#if actionForm.errors.plateNo}<p class="text-cta text-sm" role="alert">{actionForm.errors.plateNo}</p>{/if}
                </div>
                <Button
                  size="lg"
                  class="rounded-2xl"
                  disabled={actionForm.processing || !selectedState}
                  onclick={checkIn}
                >
                  {#if actionForm.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<LogIn class="size-4" aria-hidden="true" />{/if}
                  Check in
                </Button>
                <Button
                  size="lg"
                  variant="outline"
                  class="border-primary-foreground/25 bg-primary-foreground/10 hover:bg-primary-foreground/20 text-primary-foreground rounded-2xl"
                  disabled={actionForm.processing || !selectedState}
                  onclick={checkOut}
                >
                  <LogOut class="size-4" aria-hidden="true" /> Check out
                </Button>
                {#if actionForm.errors.spotId}<p class="text-cta text-sm" role="alert">{actionForm.errors.spotId}</p>{/if}
              </div>
            </div>
          </section>

          {#if grace}
            <section class="bg-card shadow-card rounded-2xl border p-4">
              <h3 class="text-muted-foreground text-[11px] font-semibold tracking-[0.14em] uppercase">Grace windows</h3>
              <dl class="mt-3 grid gap-2 text-sm">
                <div class="flex items-center justify-between gap-3">
                  <dt class="text-muted-foreground inline-flex items-center gap-1.5"><LogIn class="size-3.5" aria-hidden="true" /> Check-in ±</dt>
                  <dd class="font-mono tabular-nums">{grace.checkIn} min</dd>
                </div>
                <div class="flex items-center justify-between gap-3">
                  <dt class="text-muted-foreground inline-flex items-center gap-1.5"><TimerReset class="size-3.5" aria-hidden="true" /> Overstay cushion</dt>
                  <dd class="font-mono tabular-nums">{grace.overstay} min</dd>
                </div>
                <div class="flex items-center justify-between gap-3">
                  <dt class="text-muted-foreground inline-flex items-center gap-1.5"><ShieldAlert class="size-3.5" aria-hidden="true" /> No-show after</dt>
                  <dd class="font-mono tabular-nums">{grace.noShow} min</dd>
                </div>
              </dl>
            </section>
          {/if}

          <!-- Open sessions -->
          <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
            <h2 class="font-display px-5 pt-5 pb-3 text-base font-semibold">Open here <span class="text-muted-foreground font-mono text-sm tabular-nums">({openSessions.length})</span></h2>
            {#if openSessions.length}
              <ul class="divide-y border-t">
                {#each openSessions as session (session.id)}
                  <li class="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 py-3 text-sm">
                    <span class="font-mono text-[13px] font-semibold tabular-nums">{session.spotLabel ?? '—'}</span>
                    <span class="font-mono tracking-wide tabular-nums">{session.plateNo}</span>
                    <span class="text-muted-foreground font-mono ml-auto text-xs tabular-nums">in {fmtTime(session.enteredAtUtc)}</span>
                    {#if overdue({ enteredAtUtc: session.enteredAtUtc })}
                      <ConfirmDialog
                        title={`Resolve overstay on ${session.spotLabel ?? 'spot'}?`}
                        description="This session looks like it ran long. Ends the session now, frees the bay and notes the audit trail."
                        confirmLabel="End session"
                        onConfirm={() => resolveOverstay(session.id)}
                        triggerClass="text-destructive hover:bg-destructive/10 relative z-10 rounded-lg px-2 py-1 text-xs font-medium"
                      >
                        {#snippet trigger()}Resolve overstay{/snippet}
                      </ConfirmDialog>
                    {:else if session.cancelReason}
                      <StatusBadge status={session.status} size="xs" />
                    {:else}
                      <ConfirmDialog
                        title={`End the session on ${session.spotLabel ?? 'spot'}?`}
                        description="Frees the bay for the next booking. Use resolve notes to record why."
                        confirmLabel="Check out & free bay"
                        onConfirm={() => resolveOverstay(session.id)}
                        triggerClass="text-muted-foreground hover:bg-accent hover:text-foreground relative z-10 rounded-lg px-2 py-1 text-xs font-medium"
                      >
                        {#snippet trigger()}Resolve{/snippet}
                      </ConfirmDialog>
                    {/if}
                  </li>
                {/each}
              </ul>
            {:else}
              <p class="text-muted-foreground border-t px-5 py-8 text-center text-sm">Nothing parked right now.</p>
            {/if}
          </section>
        </aside>
      </div>
    {/if}
  </div>
{/if}
