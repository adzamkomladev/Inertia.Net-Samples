<script setup lang="ts">
import type { StoreSettings } from '@/types/management'
defineProps<{ settings: StoreSettings }>()
const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
</script>
<template>
  <details class="rounded-xl border bg-card p-4 text-sm">
    <summary class="cursor-pointer font-medium">
      Store & delivery hours · {{ settings.timeZoneId }}
    </summary>
    <div class="mt-4 grid gap-5 md:grid-cols-2">
      <div>
        <h3 class="font-semibold">{{ settings.name }}</h3>
        <p class="mt-2 text-muted-foreground">{{ settings.address }}</p>
        <a
          :href="`mailto:${settings.email}`"
          class="mt-1 block break-all text-primary underline"
          >{{ settings.email }}</a
        ><a
          v-if="settings.phone"
          :href="`tel:${settings.phone}`"
          class="mt-1 block text-primary underline"
          >{{ settings.phone }}</a
        >
        <p class="mt-3 text-xs text-muted-foreground">
          Order anytime; fulfilment follows our working and delivery hours.
        </p>
      </div>
      <div class="overflow-x-auto">
        <table class="w-full text-left text-xs">
          <thead>
            <tr>
              <th class="pb-2">Day</th>
              <th class="pb-2">Store</th>
              <th class="pb-2">Delivery</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="h in settings.hours" :key="h.day" class="border-t">
              <th class="py-2 font-medium">{{ days[h.day] }}</th>
              <td class="py-2">
                {{ h.closed ? 'Closed' : `${h.opens}–${h.closes}` }}
              </td>
              <td class="py-2">
                {{
                  h.deliveryClosed
                    ? 'No delivery'
                    : `${h.deliveryOpens}–${h.deliveryCloses}`
                }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </details>
</template>
