<script setup lang="ts">
import StatusBadge from '@/components/StatusBadge.vue'
import { money } from '@/lib/money'
import type { DeliveryView } from '@/types'

defineProps<{ delivery: DeliveryView }>()
</script>

<template>
  <section class="surface p-5" aria-labelledby="delivery-h">
    <div class="flex items-center justify-between gap-3">
      <h2 id="delivery-h" class="text-base font-semibold">Delivery</h2>
      <StatusBadge :status="delivery.status" kind="delivery" />
    </div>
    <dl class="mt-4 space-y-3 text-sm">
      <div><dt class="text-muted-foreground text-xs">Zone</dt><dd>{{ delivery.zoneName ?? '—' }}<span v-if="delivery.zoneEta" class="text-muted-foreground"> · {{ delivery.zoneEta }}</span></dd></div>
      <div v-if="delivery.riderName"><dt class="text-muted-foreground text-xs">Rider</dt><dd>{{ delivery.riderName }}<span v-if="delivery.vehicle" class="text-muted-foreground"> · {{ delivery.vehicle }}</span></dd></div>
      <div v-if="delivery.riderPhone">
        <dt class="text-muted-foreground text-xs">Rider phone</dt>
        <dd><a :href="`tel:${delivery.riderPhone}`" class="text-primary hover:underline">{{ delivery.riderPhone }}</a></dd>
      </div>
      <div v-if="delivery.cashCollected != null"><dt class="text-muted-foreground text-xs">Cash collected</dt><dd class="price font-medium">{{ money(delivery.cashCollected) }}</dd></div>
    </dl>
    <p v-if="delivery.failedReason" class="text-destructive mt-3 text-sm">{{ delivery.failedReason }}</p>
  </section>
</template>
