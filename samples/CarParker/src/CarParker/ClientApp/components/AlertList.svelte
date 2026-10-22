<script lang="ts">
  import type { Component } from 'svelte'
  import CalendarClock from '@lucide/svelte/icons/calendar-clock'
  import CarFront from '@lucide/svelte/icons/car-front'
  import ClockAlert from '@lucide/svelte/icons/clock-alert'
  import Info from '@lucide/svelte/icons/info'
  import Receipt from '@lucide/svelte/icons/receipt'
  import ShieldAlert from '@lucide/svelte/icons/shield-alert'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import Siren from '@lucide/svelte/icons/siren'
  import RelativeTime from '@/components/RelativeTime.svelte'
  import { cn } from '@/lib/utils'
  import type { AlertItem } from '@/types'

  /** Alert feed with per-kind icons; unread rows carry the amber signal edge. */
  let {
    alerts,
    class: className,
  }: {
    alerts: AlertItem[]
    class?: string
  } = $props()

  const kindIcon: Record<string, Component<Record<string, any>>> = {
    reservation_soon: CalendarClock,
    occurrence_ended: CarFront,
    overstay: ClockAlert,
    refund: Receipt,
    policy: ShieldCheck,
    incident: Siren,
    session: CarFront,
    gate: ShieldAlert,
  }

  const kindTint: Record<string, string> = {
    reservation_soon: 'bg-primary/8 text-primary dark:bg-primary/15',
    occurrence_ended: 'bg-spot-booked/12 text-spot-booked',
    overstay: 'bg-cta/18 text-cta-foreground',
    refund: 'bg-spot-free/15 text-spot-free-foreground',
    policy: 'bg-spot-booked/12 text-spot-booked',
    incident: 'bg-destructive/10 text-destructive',
    session: 'bg-spot-booked/12 text-spot-booked',
    gate: 'bg-muted text-muted-foreground',
  }
</script>

{#if !alerts.length}
  <div class="text-muted-foreground py-12 text-center text-sm">No alerts yet.</div>
{:else}
  <ul class={cn('flex flex-col', className)} aria-label="Alerts">
    {#each alerts as alert (alert.id)}
      {@const Icon = kindIcon[alert.kind] ?? Info}
      <li
        class={cn(
          'bg-card relative flex items-start gap-3.5 border-b px-4 py-3.5 transition-colors last:border-b-0 sm:px-5',
          !alert.readAt && 'bg-cta/[0.06] dark:bg-cta/[0.04]',
        )}
      >
        {#if !alert.readAt}
          <span class="bg-cta absolute top-0 bottom-0 left-0 w-0.5" aria-hidden="true"></span>
        {/if}
        <span class={cn('mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg', kindTint[alert.kind] ?? 'bg-muted text-muted-foreground')} aria-hidden="true">
          <Icon class="size-4" strokeWidth={1.75} />
        </span>
        <div class="min-w-0 flex-1">
          <p class="text-sm leading-snug font-medium">
            {alert.title}
            {#if !alert.readAt}<span class="bg-cta ml-1.5 inline-block size-1.5 rounded-full align-middle" aria-label="unread"></span>{/if}
          </p>
          {#if alert.body}<p class="text-muted-foreground mt-0.5 text-sm leading-5">{alert.body}</p>{/if}
          <p class="text-muted-foreground/80 mt-1 text-xs">
            <RelativeTime iso={alert.at} />
          </p>
        </div>
        {#if alert.href}
          <a href={alert.href} class="text-muted-foreground hover:text-foreground relative z-10 shrink-0 self-center rounded-lg px-2 py-1 text-xs font-medium hover:underline underline-offset-4">
            Open
          </a>
        {/if}
      </li>
    {/each}
  </ul>
{/if}
