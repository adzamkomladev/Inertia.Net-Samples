<script lang="ts">
  import type { Component } from 'svelte'
  import { CarFront, Clock3, CreditCard, FileText, RefreshCcw, ShieldCheck } from '@lucide/svelte'
  import PageTitle from '@/components/PageTitle.svelte'

  /** GET /about and /policies — one static page component driven by the `doc` prop. */
  let { doc }: { doc: string } = $props()

  const about = $derived(doc === 'about')

  const quickLinks: [Component<any, any, any>, string, string][] = [
    [CarFront, 'Live decks', '/lots'],
    [Clock3, 'Grace windows', '/about'],
    [ShieldCheck, 'Spot Cover claims', '/policies'],
    [CreditCard, 'Refund matrix', '/policies'],
  ]
</script>

<PageTitle title={about ? 'How it works' : 'Parking policies'} />
<article class="prose-parking mx-auto max-w-3xl">
  <p class="text-cta text-[11px] font-semibold tracking-[0.18em] uppercase">{about ? 'The platform' : 'The fine print, in plain words'}</p>
  <h1 class="mt-2">{about ? 'How CarParker works' : 'Parking policies'}</h1>

  {#if about}
    <p class="lead">
      CarParker rents parking spots by the hour, by the day or as a repeating weekly window.
      Every deck shows a live map of its bays — mint means free, violet means reserved, ink means a car is parked,
      and grey stripes mean maintenance has the bay blocked.
    </p>

    <h2>Pick a deck, pick a bay</h2>
    <p>
      Lots are organized by level and section. Each tile is a real bay with its type marked — EV charging,
      accessible, covered or compact. The map updates in real time from the guard's scanner console, so what
      you see is what the deck actually looks like.
    </p>

    <h2>Windows, not tickets</h2>
    <p>
      You reserve a <strong>window</strong>: a start and end time (single) or a weekly pattern — the same days
      and time range repeated for up to eight weeks. Billing is per started hour with a daily-max cap per 24
      hours, so an overnight booking never pays more than the day rate.
    </p>

    <h2>Arrival and departure</h2>
    <p>
      The deck's guard checks your plate against the window at check-in — you have a 15-minute grace window
      around the start. When the window ends you get a 20-minute overstay cushion before follow-up begins.
      Leave inside your window and you're done; the bay flips back to mint automatically.
    </p>

    <h2>Spot Cover</h2>
    <p>
      Every lot ships with Spot Cover: at booking you can add coverage for damage or theft while parked for
      12% of the parking total (minimum $2), up to the lot's coverage limit. Claims are filed from your
      account and decided by the operations team.
    </p>

    <h2>Cancellations and refunds</h2>
    <p>
      Cancel 24 hours or more before the window starts for a full refund; 4 hours or more for 50%; later than
      that the window is charged. Refunds are approved and paid from the operations console.
    </p>
  {:else}
    <p class="lead">
      These policies cover bookings, cancellations, refunds, Spot Cover claims and conduct on the deck.
      They apply to every CarParker lot and every window booked through the platform.
    </p>

    <h2>1. Bookings</h2>
    <p>
      A booking reserves one bay for the window you chose. Single windows run between two timestamps; recurring
      windows repeat weekly on your chosen days for up to eight weeks and are materialized into individual
      occurrences. Your plate number is shared with the deck's guard team for check-in only.
    </p>

    <h2>2. Pricing</h2>
    <p>
      Parking is billed per started hour at the lot's hourly rate. Any 24-hour slice of your window is capped
      by the lot's daily maximum. The Spot Cover premium, when selected, is 12% of the parking total with a
      $2 minimum, charged together with parking at booking.
    </p>

    <h2>3. Cancellation &amp; refunds</h2>
    <p>
      Cancel from your account. 24 hours or more before the window starts: full refund. 4 hours or more: 50%.
      Inside 4 hours or after start: the window is charged. Approved refunds are paid to the original method;
      decisions are visible on the booking page.
    </p>

    <h2>4. Overstay &amp; no-shows</h2>
    <p>
      Check-in opens 15 minutes before your window starts. A car that hasn't arrived 30 minutes after the
      window's start may be marked a no-show and the bay released. Ending late beyond the 20-minute overstay
      cushion may incur follow-up from the deck team.
    </p>

    <h2>5. Spot Cover claims</h2>
    <p>
      File claims for damage, theft or broken glass from your Spot Cover page within the policy period. Claims
      are capped at the policy's coverage amount and decided by the operations team; payouts are issued like
      refunds. Fraudulent claims void the policy.
    </p>

    <h2>6. Conduct</h2>
    <p>
      Park only in your reserved bay type, keep fire lanes clear, and follow guard instructions. Repeated
      violations can end your account. Blocked bays are off-limits at all times.
    </p>
  {/if}

  <div class="not-prose bg-card shadow-card mt-10 grid gap-4 rounded-3xl border p-6 sm:grid-cols-2">
    {#each quickLinks as [Icon, label, href] (label)}
      <a href={href} class="group bg-background hover:border-cta/50 flex items-center gap-3 rounded-2xl border p-4 transition-colors">
        <span class="bg-cta/15 text-cta-foreground flex size-9 items-center justify-center rounded-xl">
          <Icon class="size-4" strokeWidth={1.75} aria-hidden="true" />
        </span>
        <span class="text-sm font-medium">{label}</span>
        <RefreshCcw class="text-muted-foreground ml-auto size-3.5 opacity-0 transition-opacity group-hover:opacity-100" aria-hidden="true" />
      </a>
    {/each}
  </div>

  <p class="text-muted-foreground mt-8 text-xs">
    Questions? Every lot lists a contact phone; the operations team answers from the guard console.
    <FileText class="ml-1 inline size-3.5" aria-hidden="true" />
  </p>
</article>

<style>
  /* Long-form policy typography with deck-ops accents (no markdown involved). */
  .prose-parking :global(h1) { font-family: var(--font-display); font-size: 1.9rem; font-weight: 650; letter-spacing: -0.02em; }
  .prose-parking :global(h2) { font-family: var(--font-display); font-size: 1.25rem; font-weight: 650; margin-top: 2em; letter-spacing: -0.01em; }
  .prose-parking :global(p) { margin-top: 0.9em; line-height: 1.75; }
  .prose-parking :global(.lead) { font-size: 1.1rem; color: var(--muted-foreground); }
  .prose-parking :global(strong) { font-weight: 600; }
  .prose-parking :global(ol), .prose-parking :global(ul) { padding-left: 1.4em; margin-top: 0.9em; }
  .prose-parking :global(li) { margin-top: 0.3em; line-height: 1.7; }
  .prose-parking :global(li::marker) { color: var(--muted-foreground); }
</style>
