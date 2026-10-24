<script lang="ts">
  import type { Snippet } from 'svelte'
  import CalendarRange from '@lucide/svelte/icons/calendar-range'
  import CircleUserRound from '@lucide/svelte/icons/circle-user-round'
  import CreditCard from '@lucide/svelte/icons/credit-card'
  import Inbox from '@lucide/svelte/icons/inbox'
  import LayoutDashboard from '@lucide/svelte/icons/layout-dashboard'
  import MapPin from '@lucide/svelte/icons/map-pin'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import ConsoleShell, { type NavGroup } from '@/components/ConsoleShell.svelte'

  /** Customer account shell: the next booking first, history and money below. */
  let { children }: { children?: Snippet } = $props()

  const nav: NavGroup[] = [
    {
      items: [
        { href: '/account', label: 'My parking', icon: LayoutDashboard },
        { href: '/account/bookings', label: 'Bookings', icon: CalendarRange },
        { href: '/account/payments', label: 'Payments', icon: CreditCard },
        { href: '/account/insurance', label: 'Spot Cover', icon: ShieldCheck },
        { href: '/account/alerts', label: 'Alerts', icon: Inbox },
      ],
    },
    {
      label: 'More',
      items: [
        { href: '/account/profile', label: 'Profile', icon: CircleUserRound },
        { href: '/lots', label: 'Find parking', icon: MapPin },
      ],
    },
  ]
</script>

<ConsoleShell {nav} label="Account" homeHref="/account" width="max-w-5xl">
  {@render children?.()}
</ConsoleShell>
