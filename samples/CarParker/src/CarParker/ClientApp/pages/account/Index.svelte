<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import { ArrowRight, CalendarClock, CalendarRange, CarFront, Clock3, MapPin } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import RelativeTime from '@/components/RelativeTime.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import { fmtDate, fmtDateTime, fmtTime, fmtWindow } from '@/lib/time'
  import type { AccountOverview } from '@/types'

  /** GET /account: the next booking first, then a live session, a short list and the month's spend. */
  let { account }: { account: AccountOverview } = $props()

  const hasAnything = $derived(account.upcomingCount > 0 || account.activeSession !== null || account.upcoming.length > 0)
  const next = $derived(account.nextVisit)
  const weekday = (iso: string) => new Date(iso).toLocaleDateString('en-US', { weekday: 'long', timeZone: 'UTC' })
</script>

<PageTitle title="My parking" />
<div class="flex flex-col gap-6">
  <PageHeader title="My parking">
    {#snippet actions()}
      <Button href="/lots">Find parking</Button>
    {/snippet}
  </PageHeader>

  {#if !hasAnything}
    <div class="bg-card shadow-card rounded-xl">
      <EmptyState icon={CarFront} title="No bookings yet" description="Pick a lot, choose a bay and reserve it for one visit or every week.">
        <Button href="/lots">Find parking</Button>
      </EmptyState>
    </div>
  {:else}
    {#if account.activeSession}
      <section class="bg-spot-free/25 flex flex-wrap items-center gap-x-4 gap-y-1 rounded-xl p-4" aria-label="Current session">
        <CarFront class="size-6 shrink-0" aria-hidden="true" />
        <p class="min-w-0 flex-1 font-semibold">
          Parked at {account.activeSession.lotName}, bay {account.activeSession.spotLabel}
          <span class="text-muted-foreground block text-sm font-normal">Checked in {fmtDateTime(account.activeSession.enteredAtUtc)} UTC</span>
        </p>
      </section>
    {/if}

    {#if next}
      <section class="bg-card shadow-card rounded-xl p-5 sm:p-6" aria-labelledby="next-title">
        <p id="next-title" class="text-muted-foreground text-sm font-semibold tracking-wider uppercase">Next booking</p>
        <div class="mt-3 grid gap-5 sm:grid-cols-[auto_1fr] sm:items-center sm:gap-8">
          <div class="bg-primary text-primary-foreground w-fit rounded-xl px-5 py-4 text-center">
            <p class="text-sm font-semibold uppercase">{weekday(next.startAtUtc)}</p>
            <p class="font-display text-4xl leading-none font-bold tabular-nums">{fmtDate(next.startAtUtc)}</p>
          </div>
          <div class="min-w-0">
            <p class="font-display text-3xl font-bold tabular-nums">{fmtTime(next.startAtUtc)} to {fmtTime(next.endAtUtc)} <span class="text-muted-foreground text-base font-medium">UTC</span></p>
            <p class="mt-1 flex items-center gap-2 text-lg font-semibold"><MapPin class="size-5 shrink-0" aria-hidden="true" /> {next.lotName}</p>
            <p class="text-muted-foreground mt-0.5 flex items-center gap-2">
              <span class="bg-foreground text-background rounded px-1.5 text-sm font-bold">{next.spotLabel ?? '?'}</span>
              Bay {next.spotLabel ?? 'to be assigned'} · starts <RelativeTime iso={next.startAtUtc} />
            </p>
            <div class="mt-4 flex flex-wrap gap-2">
              <Button href={`/account/bookings/${next.bookingId}`}>Booking details <ArrowRight aria-hidden="true" /></Button>
            </div>
          </div>
        </div>
      </section>
    {/if}

    <section class="bg-card shadow-card overflow-hidden rounded-xl" aria-labelledby="upcoming-title">
      <header class="flex items-center justify-between px-5 pt-5 pb-3">
        <h2 id="upcoming-title" class="text-xl font-bold">Upcoming</h2>
        <Link href="/account/bookings" prefetch="hover" class="text-primary inline-flex min-h-11 items-center font-semibold hover:underline underline-offset-4">All bookings</Link>
      </header>
      {#if account.upcoming.length}
        <ul class="divide-y">
          {#each account.upcoming as booking (booking.id)}
            <li>
              <Link href={`/account/bookings/${booking.id}`} prefetch="hover" class="hover:bg-accent/50 flex items-center gap-4 px-5 py-4 transition-colors">
                <span class="text-muted-foreground" aria-hidden="true">
                  {#if booking.window === 'recurring'}<CalendarClock class="size-5" />{:else}<CalendarRange class="size-5" />{/if}
                </span>
                <div class="min-w-0 flex-1">
                  <p class="truncate font-semibold">
                    {booking.lotName}{#if booking.spotLabel}<span class="text-muted-foreground font-medium"> · bay {booking.spotLabel}</span>{/if}
                  </p>
                  <p class="text-muted-foreground text-sm tabular-nums">
                    {booking.window === 'recurring' ? `${booking.occurrenceCount} weekly visits` : fmtWindow(booking.startAtUtc, booking.endAtUtc)}
                  </p>
                </div>
                <PriceTag cents={booking.totalCents} />
                <ArrowRight class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
              </Link>
            </li>
          {/each}
        </ul>
      {:else}
        <p class="text-muted-foreground px-5 pb-8 text-center">Nothing booked ahead. <Link href="/lots" class="text-primary font-semibold underline underline-offset-4">Find parking</Link></p>
      {/if}
    </section>

    <dl class="text-muted-foreground flex flex-wrap gap-x-8 gap-y-2 px-1 text-sm">
      <div class="flex items-center gap-2"><Clock3 class="size-4" aria-hidden="true" /><dt>Spent in the last 30 days</dt><dd class="text-foreground font-semibold tabular-nums">{money(account.spend30dCents)}</dd></div>
      <div class="flex items-center gap-2">
        <dt>Unread alerts</dt>
        <dd class="text-foreground font-semibold tabular-nums">{account.unreadAlerts}</dd>
        {#if account.unreadAlerts}<Link href="/account/alerts" class="text-primary font-semibold hover:underline">Read</Link>{/if}
      </div>
    </dl>
  {/if}
</div>
