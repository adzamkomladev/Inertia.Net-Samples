<script lang="ts">
  import type { Snippet } from 'svelte'
  import { usePage } from '@inertiajs/svelte'
  import BarChart3 from '@lucide/svelte/icons/chart-column'
  import CalendarRange from '@lucide/svelte/icons/calendar-range'
  import CarFront from '@lucide/svelte/icons/car-front'
  import CircleDollarSign from '@lucide/svelte/icons/circle-dollar-sign'
  import Car from '@lucide/svelte/icons/car'
  import LayoutDashboard from '@lucide/svelte/icons/layout-dashboard'
  import ScrollText from '@lucide/svelte/icons/scroll-text'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import UsersRound from '@lucide/svelte/icons/users-round'
  import ConsoleShell, { type NavGroup } from '@/components/ConsoleShell.svelte'

  /** Admin shell: grouped sidebar; attention counts ride on the overview props and badge the queues. */
  let { children }: { children?: Snippet } = $props()

  const page = usePage()
  // Counts arrive with the overview page; keep the last ones so the badges stay while browsing queues.
  let attention = $state<Record<string, number> | undefined>()
  $effect(() => {
    const fresh = (page.props as { overview?: { needsAttention?: Record<string, number> } }).overview?.needsAttention
    if (fresh) attention = fresh
  })

  const nav = $derived<NavGroup[]>([
    {
      items: [
        { href: '/admin', label: 'Overview', icon: LayoutDashboard },
        { href: '/admin/analytics', label: 'Analytics', icon: BarChart3 },
      ],
    },
    {
      label: 'Operations',
      items: [
        { href: '/admin/lots', label: 'Lots & spots', icon: Car },
        { href: '/admin/bookings', label: 'Bookings', icon: CalendarRange },
        { href: '/admin/sessions', label: 'Sessions', icon: CarFront, badge: attention?.overdueSessions },
        { href: '/admin/incidents', label: 'Incidents', icon: ScrollText, badge: attention?.openIncidents },
      ],
    },
    {
      label: 'Money & cover',
      items: [
        { href: '/admin/refunds', label: 'Refunds', icon: CircleDollarSign, badge: attention?.refundsPending },
        { href: '/admin/insurance', label: 'Spot Cover claims', icon: ShieldCheck, badge: attention?.claimsPending },
      ],
    },
    { label: 'People', items: [{ href: '/admin/team', label: 'Team', icon: UsersRound }, { href: '/admin/users', label: 'Users', icon: UsersRound }] },
  ])
</script>

<ConsoleShell {nav} label="Admin" homeHref="/admin" width="max-w-6xl">
  {@render children?.()}
</ConsoleShell>
