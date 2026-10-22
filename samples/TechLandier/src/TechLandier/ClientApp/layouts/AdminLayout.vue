<script setup lang="ts">
import {
  BarChart3, CreditCard, LayoutDashboard, LifeBuoy, Package, ShieldCheck, ShoppingCart, Star, Truck,
} from '@lucide/vue'
import SidebarShell, { type NavGroup } from '@/components/SidebarShell.vue'

const groups: NavGroup[] = [
  { items: [{ href: '/admin', label: 'Overview', icon: LayoutDashboard, exact: true }, { href: '/admin/analytics', label: 'Analytics', icon: BarChart3 }] },
  {
    label: 'Sales',
    items: [
      { href: '/admin/orders', label: 'Orders', icon: ShoppingCart },
      { href: '/admin/deliveries', label: 'Deliveries', icon: Truck },
      { href: '/admin/catalog', label: 'Catalog', icon: Package },
    ],
  },
  {
    label: 'Customers',
    items: [
      { href: '/admin/credit', label: 'Installment plans', icon: CreditCard },
      { href: '/admin/care', label: 'Care desk', icon: ShieldCheck },
      { href: '/admin/support', label: 'Support', icon: LifeBuoy },
      { href: '/admin/reviews', label: 'Reviews', icon: Star },
    ],
  },
]
</script>

<template>
  <SidebarShell :groups="groups" area="Admin" palette width="max-w-7xl"><slot /></SidebarShell>
</template>
