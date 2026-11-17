<script setup lang="ts">
import { Link } from '@inertiajs/vue3'
import { MoreHorizontal, Phone, StickyNote, Truck, XCircle } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import type { DeliveryRow } from '@/types'

defineProps<{ delivery: DeliveryRow }>()
defineEmits<{ note: []; fail: [] }>()
</script>

<!-- Per-run overflow menu: open the order, call the customer, note, log a failed attempt. -->
<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button size="icon-sm" variant="ghost" class="pointer-coarse:size-11" :aria-label="`More actions for ${delivery.orderNumber}`"><MoreHorizontal /></Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-48">
      <DropdownMenuItem as-child><Link :href="`/admin/orders/${delivery.orderNumber}`"><Truck /> Open order</Link></DropdownMenuItem>
      <DropdownMenuItem v-if="delivery.phone" as-child><a :href="`tel:${delivery.phone}`"><Phone /> Call customer</a></DropdownMenuItem>
      <DropdownMenuItem @select="$emit('note')"><StickyNote /> {{ delivery.note ? 'Edit note' : 'Add note' }}</DropdownMenuItem>
      <template v-if="delivery.status === 'assigned' || delivery.status === 'picked_up'">
        <DropdownMenuSeparator />
        <DropdownMenuItem class="text-destructive focus:text-destructive" @select="$emit('fail')"><XCircle class="text-destructive" /> Log failed attempt</DropdownMenuItem>
      </template>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
