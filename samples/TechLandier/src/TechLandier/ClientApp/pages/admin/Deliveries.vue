<script setup lang="ts">
import { Head, router, useForm } from '@inertiajs/vue3'
import { ref } from 'vue'
import { Bike, MapPin, Plus, Truck } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import DataTable from '@/components/DataTable.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { DeliveryRow, RiderAdminRow, ZoneAdminRow } from '@/types'

const props = defineProps<{
  deliveries: DeliveryRow[]
  riders: RiderAdminRow[]
  zones: ZoneAdminRow[]
}>()

const assignForm = useForm({ riderId: '' })
const target = ref<string | null>(null)

const act = (id: string, action: 'pickup' | 'deliver' | 'fail') => {
  router.post(
    `/admin/deliveries/${id}/${action}`,
    action === 'fail' ? { reason: 'Address unreachable after two attempts' } : {},
    { preserveScroll: true },
  )
}
const assign = (id: string) => router.post(`/admin/deliveries/${id}/assign`, { riderId: assignForm.riderId }, { onFinish: () => { target.value = null; assignForm.riderId = '' } })

const riderForm = useForm({ name: '', phone: '', vehicle: '', service: 'in_house' })
const zoneForm = useForm({ name: '', fee: 5, etaText: 'Same day if ordered by 2pm', allowPayOnDelivery: true })
const toggleZone = (z: ZoneAdminRow) =>
  router.post(`/admin/zones/${z.id}`, { name: z.name, fee: z.fee, etaText: z.etaText, allowPayOnDelivery: z.allowPayOnDelivery, isActive: !z.isActive }, { preserveScroll: true })
</script>

<template>
  <Head title="Deliveries" />
    <Head title="Deliveries" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Deliveries & riders" description="Assignment board plus the rider and zone registry. Pay-on-delivery cash reconciles here." />

    <!-- board -->
    <section class="bg-card shadow-card overflow-hidden rounded-2xl border">
      <div class="flex items-center gap-2 border-b p-3">
        <Truck class="size-4" />
        <h2 class="font-display text-sm font-semibold">Assignment board</h2>
        <span class="text-muted-foreground ml-auto text-xs">{{ deliveries.filter((d) => !d.rider).length }} waiting for a rider</span>
      </div>
      <DataTable
        :columns="[
          { key: 'order', label: 'Order', primary: true },
          { key: 'zone', label: 'Zone' },
          { key: 'rider', label: 'Rider' },
          { key: 'payment', label: 'Cash', align: 'right' },
          { key: 'status', label: 'Status', align: 'right' },
          { key: 'actions', header: '', align: 'right' },
        ]"
        :rows="(deliveries as unknown as Record<string, unknown>[])"
        row-key="id"
        empty-title="Nothing to dispatch"
      >
        <template #cell-order="{ row }">
          <a :href="`/admin/orders/${row.orderNumber}`" class="font-mono text-sm font-semibold hover:underline">{{ row.orderNumber }}</a>
          <span class="text-muted-foreground block text-xs">{{ row.paymentMethod.replace('_', ' ') }} · {{ money(row.total) }}</span>
        </template>
        <template #cell-zone="{ row }"><span>{{ row.zone ?? '—' }}</span></template>
        <template #cell-rider="{ row }">
          <template v-if="row.rider">{{ row.rider }}</template>
          <template v-else>
            <form
              v-if="target === row.id"
              class="flex items-center gap-1"
              @submit.prevent="assign(row.id as unknown as string)"
            >
              <Select v-model="assignForm.riderId">
                <SelectTrigger class="w-36"><SelectValue placeholder="Rider" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="r in riders.filter((x) => x.isActive)" :key="r.id" :value="r.id">{{ r.name }}</SelectItem>
                </SelectContent>
              </Select>
              <Button type="submit" size="xs" :disabled="!assignForm.riderId || assignForm.processing">Go</Button>
            </form>
            <Button v-else size="xs" variant="outline" class="rounded-full" @click="target = row.id">Assign</Button>
          </template>
        </template>
        <template #cell-payment="{ row }">
          <span v-if="row.cashCollected != null" class="font-mono text-xs">{{ money(row.cashCollected) }} cash</span>
          <span v-else class="text-muted-foreground text-xs">paid online</span>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge :status="row.status" kind="delivery" size="xs" />
        </template>
        <template #cell-actions="{ row }">
          <div v-if="row.status === 'assigned'" class="flex justify-end gap-1">
            <Button size="xs" variant="outline" class="rounded-full" @click="act(row.id, 'pickup')">Picked up</Button>
            <Button size="xs" variant="ghost" class="text-destructive rounded-full" @click="act(row.id, 'fail')">Fail</Button>
          </div>
          <Button v-else-if="row.status === 'picked_up'" size="xs" variant="outline" class="rounded-full" @click="act(row.id, 'deliver')">Delivered</Button>
          <span v-else-if="row.failedReason" class="text-destructive block max-w-44 text-[11px] leading-4">{{ row.failedReason }}</span>
          <span v-else-if="row.deliveredAt" class="text-muted-foreground text-xs">{{ fmtDate(row.deliveredAt, true) }}</span>
        </template>
      </DataTable>
    </section>

    <div class="grid gap-5 lg:grid-cols-2">
      <!-- riders -->
      <section class="bg-card shadow-card rounded-2xl border p-5">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Bike class="size-4" /> Riders</h2>
        <form class="mt-3 flex flex-wrap items-end gap-2" @submit.prevent="riderForm.post('/admin/riders', { preserveScroll: true, onFinish: () => riderForm.reset() })">
          <Input v-model="riderForm.name" class="w-36" placeholder="Name" maxlength="60" required />
          <Input v-model="riderForm.phone" class="w-36" placeholder="+233 …" maxlength="20" required />
          <Select v-model="riderForm.service">
            <SelectTrigger class="w-32"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="in_house">In-house</SelectItem>
              <SelectItem value="yango">Yango</SelectItem>
              <SelectItem value="bolt">Bolt</SelectItem>
              <SelectItem value="uber">Uber</SelectItem>
              <SelectItem value="other">Other</SelectItem>
            </SelectContent>
          </Select>
          <Button type="submit" size="sm" variant="outline" :disabled="riderForm.processing"><Plus class="size-3.5" /> Add</Button>
        </form>
        <ul class="mt-3 divide-y">
          <li v-for="r in riders" :key="r.id" class="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm first:pt-0">
            <span class="font-medium">{{ r.name }}</span>
            <span class="text-muted-foreground font-mono text-xs">{{ r.phone }}</span>
            <span class="text-muted-foreground">{{ r.vehicle ?? r.service }}</span>
            <StatusBadge v-if="!r.isActive" status="inactive" kind="delivery" size="xs" />
          </li>
        </ul>
      </section>

      <!-- zones -->
      <section class="bg-card shadow-card rounded-2xl border p-5">
        <h2 class="font-display flex items-center gap-2 text-base font-semibold"><MapPin class="size-4" /> Zones</h2>
        <form class="mt-3 flex flex-wrap items-end gap-2" @submit.prevent="zoneForm.post('/admin/zones', { preserveScroll: true, onFinish: () => zoneForm.reset() })">
          <Input v-model="zoneForm.name" class="w-36" placeholder="Zone name" maxlength="60" required />
          <Input v-model="zoneForm.fee" type="number" min="0" step="0.5" class="w-20" placeholder="Fee" required />
          <Input v-model="zoneForm.etaText" class="w-44" placeholder="Same day if ordered by 2pm" maxlength="60" required />
          <Button type="submit" size="sm" variant="outline" :disabled="zoneForm.processing"><Plus class="size-3.5" /> Add</Button>
        </form>
        <ul class="mt-3 divide-y">
          <li v-for="z in zones" :key="z.id" class="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm first:pt-0">
            <span class="font-medium">{{ z.name }}</span>
            <span class="text-muted-foreground">{{ z.etaText }}</span>
            <span class="text-cta mr-auto text-[11px] font-medium" v-if="z.allowPayOnDelivery">PoD ok</span>
            <span class="font-mono text-xs">{{ money(z.fee) }}</span>
            <button class="text-muted-foreground hover:text-destructive text-xs" @click="toggleZone(z)">
              {{ z.isActive ? 'deactivate' : 'reactivate' }}
            </button>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>
