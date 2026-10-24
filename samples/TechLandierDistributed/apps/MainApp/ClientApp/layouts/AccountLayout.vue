<script setup lang="ts">
import { Bell, CreditCard, LayoutGrid, LifeBuoy, Package, ShieldCheck, SlidersHorizontal, Smartphone, Wrench } from '@lucide/vue'
import SidebarShell, { type NavGroup } from '@/components/SidebarShell.vue'

const groups: NavGroup[] = [
  {
    items: [
      { href: '/account', label: 'Overview', icon: LayoutGrid, exact: true },
      { href: '/account/orders', label: 'Orders', icon: Package },
      { href: '/account/credit', label: 'Installments', icon: CreditCard },
    ],
  },
  {
    label: 'Care',
    items: [
      { href: '/account/care/warranties', label: 'Warranties', icon: ShieldCheck },
      { href: '/account/care/insurance', label: 'Insurance', icon: Smartphone },
      { href: '/account/care/repairs', label: 'Repairs', icon: Wrench },
    ],
  },
  {
    label: 'Help',
    items: [
      { href: '/account/support', label: 'Support', icon: LifeBuoy },
      { href: '/account/alerts', label: 'Alerts', icon: Bell },
      { href: '/account/preferences', label: 'Preferences', icon: SlidersHorizontal },
    ],
  },
]
</script>

<template>
  <SidebarShell :groups="groups" area="Account" width="max-w-5xl"><slot /></SidebarShell>
</template>
