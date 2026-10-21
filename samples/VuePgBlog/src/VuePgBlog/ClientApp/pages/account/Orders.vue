<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3'
import { PackageOpen } from '@lucide/vue'
import { Badge } from '@/components/ui/badge'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import PriceTag from '@/components/PriceTag.vue'
import { fmtDate } from '@/lib/time'
import type { AccountOrderRow } from '@/types'

defineProps<{ orders: AccountOrderRow[] }>()
</script>

<template>
  <Head title="My orders" />
    <Head title="My orders" />

  <div class="flex flex-col gap-6">
    <PageHeader title="My orders" description="Everything you've bought — newest first, with review CTAs on delivered items." />

    <div v-if="orders.length" class="flex flex-col gap-3">
      <Link
        v-for="o in orders"
        :key="o.id"
        :href="`/account/orders/${o.number}`"
        class="bg-card shadow-card hover:border-primary/30 block rounded-2xl border p-4 transition-colors sm:p-5"
      >
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <span class="font-mono text-sm font-semibold">{{ o.number }}</span>
          <StatusBadge :status="o.status" kind="order" size="xs" />
          <StatusBadge :status="o.paymentStatus" kind="payment" size="xs" />
          <span class="text-muted-foreground ml-auto text-xs">{{ fmtDate(o.createdAt, true) }}</span>
          <PriceTag :price="o.total" size="sm" />
        </div>
        <p class="text-muted-foreground mt-1.5 truncate text-sm">{{ o.itemNames.join(' · ') }}</p>
        <div class="mt-2 flex flex-wrap items-center gap-2 text-xs">
          <span class="text-muted-foreground">{{ o.paymentMethod.replace('_', ' ') }}</span>
          <Badge v-if="o.hasReview" variant="outline" class="h-5">reviewed</Badge>
          <span v-else-if="o.status === 'delivered'" class="text-cta inline-flex items-center gap-1 font-medium">
            Rate it — your review helps the next buyer
          </span>
        </div>
      </Link>
    </div>

    <EmptyState
      v-else
      :icon="PackageOpen"
      title="No orders yet"
      description="When you place your first order it lands here with its full timeline."
    />
  </div>
</template>
