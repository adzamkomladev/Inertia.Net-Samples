<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import { ArrowRight, CalendarRange, CarFront, CircleDollarSign, ClockAlert, ReceiptText, ScrollText, ShieldAlert, TimerOff } from '@lucide/svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatCard from '@/components/StatCard.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { money } from '@/lib/money'
  import type { AdminOverview } from '@/types'

  /** GET /admin — KPI cards + needs-attention counts with links. */
  let { overview }: { overview: AdminOverview } = $props()

  const attention = $derived(overview.needsAttention)
  const attentionTotal = $derived(attention.refundsPending + attention.claimsPending + attention.overdueSessions + attention.openIncidents)
</script>

<PageTitle title="Admin overview" />
<div class="flex flex-col gap-6">
  <PageHeader title="Operations overview" description="Today's deck activity and everything that needs a decision.">
    {#snippet actions()}<Button href="/admin/analytics" size="sm" class="rounded-2xl">Analytics <ArrowRight class="size-4" aria-hidden="true" /></Button>{/snippet}
  </PageHeader>

  <!-- Needs attention -->
  <section class="bg-primary text-primary-foreground shadow-lifted relative overflow-hidden rounded-3xl p-6">
    <div class="deck-grid absolute inset-0 opacity-30" aria-hidden="true"></div>
    <div class="relative">
      <div class="flex items-center justify-between gap-4">
        <div>
          <p class="text-cta text-[11px] font-semibold tracking-[0.18em] uppercase">Needs attention</p>
          <p class="font-display mt-1 text-2xl font-semibold tabular-nums">
            {attentionTotal} open item{attentionTotal === 1 ? '' : 's'}
          </p>
        </div>
      </div>
      <div class="mt-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        {#each [
          { count: attention.refundsPending, label: 'Pending refunds', href: '/admin/refunds', icon: ReceiptText },
          { count: attention.claimsPending, label: 'Spot Cover claims', href: '/admin/insurance', icon: ShieldAlert },
          { count: attention.overdueSessions, label: 'Overdue sessions', href: '/admin/sessions', icon: TimerOff },
          { count: attention.openIncidents, label: 'Open incidents', href: '/admin/incidents', icon: ScrollText },
        ] as item (item.label)}
          <Link href={item.href} class="bg-primary-foreground/10 hover:bg-primary-foreground/15 rounded-2xl p-4 transition-colors">
            <span class="text-primary-foreground/60 flex items-center gap-2 text-xs font-medium">
              <item.icon class="size-3.5" aria-hidden="true" /> {item.label}
            </span>
            <span class="font-display mt-1 block text-xl font-semibold tabular-nums" class:text-cta={item.count > 0}>{item.count}</span>
          </Link>
        {/each}
      </div>
    </div>
  </section>

  <!-- Today -->
  <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
    <StatCard label="Reservations today" value={overview.today.reservations} icon={CalendarRange} />
    <StatCard label="Gate sessions today" value={overview.today.sessions} icon={CarFront} />
    <StatCard label="Revenue today" value={money(overview.today.revenueCents)} icon={CircleDollarSign} />
    <StatCard label="No-shows · 24h" value={attention.noShowsToday} icon={ClockAlert} hint="windows released after 30 min" />
  </div>

  <!-- Lots -->
  <section class="flex flex-col gap-4">
    <div class="flex items-end justify-between gap-4">
      <div>
        <h2 class="font-display text-lg font-semibold">Decks</h2>
        <p class="text-muted-foreground text-sm">Live occupancy across every open lot.</p>
      </div>
      <Link href="/admin/lots" class="text-muted-foreground hover:text-foreground text-sm font-medium hover:underline underline-offset-4">Manage lots</Link>
    </div>
    {#if overview.lots.length}
      <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
        {#each overview.lots as lot (lot.id)}
          <LotCard {lot} href={`/admin/lots/${lot.id}`} />
        {/each}
      </div>
    {:else}
      <div class="rounded-3xl border border-dashed p-12 text-center">
        <p class="font-display font-semibold">No open decks</p>
        <p class="text-muted-foreground mt-1 text-sm">Create the first lot to open bookings.</p>
        <Button href="/admin/lots" class="mt-4 rounded-2xl">Create a lot</Button>
      </div>
    {/if}
  </section>
</div>
