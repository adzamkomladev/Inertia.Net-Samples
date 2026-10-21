<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import { ArrowRight, CalendarClock, CalendarRange, CarFront, CircleDollarSign, Receipt } from '@lucide/svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import StatCard from '@/components/StatCard.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import { money } from '@/lib/money'
  import { fmtDateTime, fmtWindow } from '@/lib/time'
  import type { AccountOverview } from '@/types'

  /** GET /account — overview: next visit, active session, spend, upcoming windows. */
  let { account }: { account: AccountOverview } = $props()

  const hasAnything = $derived(account.upcomingCount > 0 || account.activeSession !== null || account.upcoming.length > 0)
</script>

<PageTitle title="My parking" />
<div class="flex flex-col gap-6">
  <PageHeader title="My parking" description="Your windows, sessions and spending at a glance.">
    {#snippet actions()}
      <Button href="/lots" class="rounded-2xl">Find parking <ArrowRight class="size-4" aria-hidden="true" /></Button>
    {/snippet}
  </PageHeader>

  {#if !hasAnything}
    <div class="bg-card shadow-card rounded-3xl border">
      <EmptyState
        icon={CarFront}
        title="No bookings yet"
        description="Pick a live deck, choose your bay and reserve a window — single or recurring weekly."
        class="py-12"
      >
        <Button href="/lots" class="rounded-2xl">Browse decks</Button>
      </EmptyState>
    </div>
  {:else}
    <!-- KPI row -->
    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <StatCard label="Upcoming windows" value={account.upcomingCount} icon={CalendarRange} />
      <StatCard label="Spent · 30 days" value={money(account.spend30dCents)} icon={CircleDollarSign} />
      <StatCard label="Unread alerts" value={account.unreadAlerts} icon={Receipt} hint={account.unreadAlerts ? 'new since your last visit' : 'all caught up'} />
      <StatCard label="Open session" value={account.activeSession ? 'Live' : 'None'} icon={CarFront} hint={account.activeSession ? `since ${fmtDateTime(account.activeSession.enteredAtUtc)}` : 'roll in during a window'} />
    </div>

    <!-- Active session -->
    {#if account.activeSession}
      <section class="bg-spot-free/10 border-spot-free/40 rounded-2xl border p-5">
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div class="flex items-center gap-3">
            <span class="bg-spot-free text-spot-free-foreground flex size-10 items-center justify-center rounded-2xl" aria-hidden="true">
              <CarFront class="size-5" />
            </span>
            <div>
              <p class="font-medium">Parked at {account.activeSession.lotName} — spot {account.activeSession.spotLabel}</p>
              <p class="text-muted-foreground text-[13px]">Checked in {fmtDateTime(account.activeSession.enteredAtUtc)}</p>
            </div>
          </div>
          <span class="bg-spot-free text-spot-free-foreground inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium">
            <span class="animate-pulse-soft size-1.5 rounded-full bg-current" aria-hidden="true"></span> Session live
          </span>
        </div>
      </section>
    {/if}

    <!-- Next visit -->
    {#if account.nextVisit}
      <section class="bg-primary text-primary-foreground shadow-lifted relative overflow-hidden rounded-3xl p-6">
        <div class="deck-grid absolute inset-0 opacity-30" aria-hidden="true"></div>
        <div class="relative flex flex-wrap items-center justify-between gap-4">
          <div class="min-w-0">
            <p class="text-cta text-[11px] font-semibold tracking-[0.18em] uppercase">Next visit</p>
            <p class="font-display mt-1 text-xl font-semibold">{account.nextVisit.lotName} — spot {account.nextVisit.spotLabel}</p>
            <p class="text-primary-foreground/70 mt-1 font-mono text-sm tabular-nums">
              {fmtWindow(account.nextVisit.startAtUtc, account.nextVisit.endAtUtc)}
            </p>
          </div>
          <Link
            href={`/account/bookings/${account.nextVisit.bookingId}`}
            class="text-cta-foreground bg-cta inline-flex h-10 items-center gap-2 rounded-2xl px-5 text-sm font-medium transition-transform hover:bg-cta/90 active:scale-[0.98]"
          >Booking details <ArrowRight class="size-4" aria-hidden="true" /></Link>
        </div>
      </section>
    {/if}

    <!-- Upcoming list -->
    <section class="bg-card shadow-card overflow-hidden rounded-3xl border">
      <header class="flex items-center justify-between px-5 pt-5 pb-3">
        <h2 class="font-display text-base font-semibold">Upcoming bookings</h2>
        <Link href="/account/bookings" class="text-muted-foreground hover:text-foreground text-xs font-medium hover:underline underline-offset-4">All bookings</Link>
      </header>
      {#if account.upcoming.length}
        <ul class="divide-y border-t">
          {#each account.upcoming as booking (booking.id)}
            <li>
              <Link href={`/account/bookings/${booking.id}`} class="hover:bg-accent/40 flex items-center gap-4 px-5 py-3.5 transition-colors">
                <span class="bg-muted text-muted-foreground flex size-9 shrink-0 items-center justify-center rounded-xl" aria-hidden="true">
                  {#if booking.window === 'recurring'}<CalendarClock class="size-4" />{:else}<CalendarRange class="size-4" />{/if}
                </span>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm font-medium">
                    {booking.lotName}
                    {#if booking.spotLabel}<span class="text-muted-foreground"> · spot {booking.spotLabel}</span>{/if}
                  </p>
                  <p class="text-muted-foreground mt-0.5 font-mono text-xs tabular-nums">
                    {booking.window === 'recurring'
                      ? `${booking.occurrenceCount} windows`
                      : fmtWindow(booking.startAtUtc, booking.endAtUtc)}
                  </p>
                </div>
                <PriceTag cents={booking.totalCents} />
                <ArrowRight class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
              </Link>
            </li>
          {/each}
        </ul>
      {:else}
        <p class="text-muted-foreground border-t px-5 py-8 text-center text-sm">No upcoming windows — time to plan the next trip.</p>
      {/if}
    </section>
  {/if}
</div>
