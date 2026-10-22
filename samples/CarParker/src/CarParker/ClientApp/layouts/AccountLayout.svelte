<script lang="ts">
  import type { Snippet } from 'svelte'
  import { CalendarRange, CircleUserRound, CreditCard, Inbox, LayoutDashboard, MapPin, ShieldCheck } from '@lucide/svelte'
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
