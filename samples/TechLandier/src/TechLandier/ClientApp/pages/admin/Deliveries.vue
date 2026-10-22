<script setup lang="ts">
import { Head, router, useForm } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { Bike, MapPin } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import DataTable from '@/components/DataTable.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { DeliveryRow, RiderAdminRow, ZoneAdminRow } from '@/types'

const props = defineProps<{
  deliveries: DeliveryRow[]
  riders: RiderAdminRow[]
  zones: ZoneAdminRow[]
}>()

const lanes = {
  needs: (d: DeliveryRow) => d.status === 'unassigned',
  active: (d: DeliveryRow) =>
    d.status === 'assigned' || d.status === 'picked_up',
  done: (d: DeliveryRow) => d.status === 'delivered' || d.status === 'failed',
}
type Lane = keyof typeof lanes
const count = (l: Lane) => props.deliveries.filter(lanes[l]).length
const lane = ref<string>(
  count('needs') > 0 ? 'needs' : count('active') > 0 ? 'active' : 'done',
)
const limit = ref(40)
const laneRows = computed(() =>
  props.deliveries.filter(lanes[lane.value as Lane]),
)
const visible = computed(() => laneRows.value.slice(0, limit.value))

const assignForm = useForm({ riderId: '' })
const target = ref<string | null>(null)

const act = (id: string, action: 'pickup' | 'deliver' | 'fail') => {
  router.post(
    `/admin/deliveries/${id}/${action}`,
    action === 'fail'
      ? { reason: 'Address unreachable after two attempts' }
      : {},
    { preserveScroll: true },
  )
}
const assign = (id: string) =>
  router.post(
    `/admin/deliveries/${id}/assign`,
    { riderId: assignForm.riderId },
    {
      onFinish: () => {
        target.value = null
        assignForm.riderId = ''
      },
    },
  )

const riderForm = useForm({
  name: '',
  phone: '',
  vehicle: '',
  service: 'in_house',
})
const editingRider = ref<string | null>(null)
const editingZone = ref<string | null>(null)
const riderActive = ref(true)
const zoneActive = ref(true)
const zoneForm = useForm({
  name: '',
  fee: 5,
  etaText: 'Same day if ordered by 2pm',
  allowPayOnDelivery: true,
})
function cancelRider() {
  editingRider.value = null
  riderForm.reset()
  riderActive.value = true
}
function cancelZone() {
  editingZone.value = null
  zoneForm.reset()
  zoneActive.value = true
}
function editRider(r: RiderAdminRow) {
  editingRider.value = r.id
  Object.assign(riderForm, {
    name: r.name,
    phone: r.phone,
    vehicle: r.vehicle ?? '',
    service: r.service,
  })
  riderActive.value = r.isActive
}
function editZone(z: ZoneAdminRow) {
  editingZone.value = z.id
  Object.assign(zoneForm, {
    name: z.name,
    fee: z.fee,
    etaText: z.etaText,
    allowPayOnDelivery: z.allowPayOnDelivery,
  })
  zoneActive.value = z.isActive
}
function saveRider() {
  riderForm
    .transform((data) => ({ ...data, isActive: riderActive.value }))
    .post(
      editingRider.value
        ? `/admin/riders/${editingRider.value}`
        : '/admin/riders',
      {
        preserveScroll: true,
        onSuccess: () => {
          riderForm.reset()
          editingRider.value = null
          riderActive.value = true
        },
      },
    )
}
function saveZone() {
  zoneForm
    .transform((data) => ({ ...data, isActive: zoneActive.value }))
    .post(
      editingZone.value ? `/admin/zones/${editingZone.value}` : '/admin/zones',
      {
        preserveScroll: true,
        onSuccess: () => {
          zoneForm.reset()
          editingZone.value = null
          zoneActive.value = true
        },
      },
    )
}
const toggleZone = (z: ZoneAdminRow) =>
  router.post(
    `/admin/zones/${z.id}`,
    {
      name: z.name,
      fee: z.fee,
      etaText: z.etaText,
      allowPayOnDelivery: z.allowPayOnDelivery,
      isActive: !z.isActive,
    },
    { preserveScroll: true },
  )
</script>

<template>
  <Head title="Deliveries" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="Deliveries & riders"
      description="Assign riders, track each delivery and manage zones. Cash on delivery is reconciled here."
    />

    <!-- board -->
    <section class="surface overflow-hidden">
      <div class="flex flex-wrap items-center gap-3 border-b p-3">
        <h2 class="sr-only">Assignment board</h2>
        <SegmentedControl
          v-model="lane"
          label="Delivery stage"
          :options="[
            { value: 'needs', label: `Needs a rider (${count('needs')})` },
            { value: 'active', label: `On the road (${count('active')})` },
            { value: 'done', label: `Finished (${count('done')})` },
          ]"
        />
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
        :rows="visible as unknown as Record<string, unknown>[]"
        row-key="id"
        empty-title="Nothing in this stage"
        empty-description="Deliveries move here as orders are packed, assigned and completed."
      >
        <template #cell-order="{ row }">
          <a
            :href="`/admin/orders/${row.orderNumber}`"
            class="tabular-nums text-sm font-semibold hover:underline"
            >{{ row.orderNumber }}</a
          >
          <span class="text-muted-foreground block text-xs"
            >{{ row.paymentMethod.replace(/_/g, ' ') }} ·
            {{ money(row.total) }}</span
          >
        </template>
        <template #cell-zone="{ row }"
          ><span>{{ row.zone ?? '—' }}</span></template
        >
        <template #cell-rider="{ row }">
          <template v-if="row.rider">{{ row.rider }}</template>
          <template v-else>
            <form
              v-if="target === row.id"
              class="flex items-center gap-1"
              @submit.prevent="assign(row.id as unknown as string)"
            >
              <Select v-model="assignForm.riderId">
                <SelectTrigger class="w-36"
                  ><SelectValue placeholder="Rider"
                /></SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="r in riders.filter((x) => x.isActive)"
                    :key="r.id"
                    :value="r.id"
                    >{{ r.name }}</SelectItem
                  >
                </SelectContent>
              </Select>
              <Button
                type="submit"
                size="xs"
                :disabled="!assignForm.riderId || assignForm.processing"
                >Go</Button
              >
            </form>
            <Button v-else size="xs" variant="outline" @click="target = row.id"
              >Assign</Button
            >
          </template>
        </template>
        <template #cell-payment="{ row }">
          <span v-if="row.cashCollected != null" class="tabular-nums text-xs"
            >{{ money(row.cashCollected) }} cash</span
          >
          <span
            v-else-if="row.paymentMethod === 'pay_on_delivery'"
            class="tabular-nums text-xs"
            >{{ money(row.total) }} to collect</span
          >
          <span v-else class="text-muted-foreground text-xs">paid online</span>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge :status="row.status" kind="delivery" />
        </template>
        <template #cell-actions="{ row }">
          <div v-if="row.status === 'assigned'" class="flex justify-end gap-1">
            <Button size="xs" variant="outline" @click="act(row.id, 'pickup')"
              >Picked up</Button
            >
            <Button
              size="xs"
              variant="ghost"
              class="text-destructive"
              @click="act(row.id, 'fail')"
              >Fail</Button
            >
          </div>
          <Button
            v-else-if="row.status === 'picked_up'"
            size="xs"
            variant="outline"
            @click="act(row.id, 'deliver')"
            >Delivered</Button
          >
          <span
            v-else-if="row.failedReason"
            class="text-destructive block max-w-44 text-[11px] leading-4"
            >{{ row.failedReason }}</span
          >
          <span
            v-else-if="row.deliveredAt"
            class="text-muted-foreground text-xs"
            >{{ fmtDate(row.deliveredAt, true) }}</span
          >
        </template>
      </DataTable>
      <div v-if="laneRows.length > limit" class="border-t p-3 text-center">
        <Button variant="ghost" size="sm" @click="limit += 40"
          >Show {{ Math.min(40, laneRows.length - limit) }} more</Button
        >
      </div>
    </section>

    <div class="grid gap-5 lg:grid-cols-2">
      <!-- riders -->
      <section class="surface p-5">
        <h2 class="flex items-center gap-2 text-base font-semibold">
          <Bike class="size-4" /> Riders
        </h2>
        <p class="mt-1 text-sm text-muted-foreground">
          Manage driver contact details, vehicles and availability.
        </p>
        <form
          class="mt-5 grid gap-4 rounded-xl border bg-muted/20 p-4 sm:grid-cols-2"
          @submit.prevent="saveRider"
        >
          <h3 class="font-medium sm:col-span-2">
            {{ editingRider ? 'Edit driver' : 'Add driver' }}
          </h3>
          <div class="space-y-2">
            <Label for="driver-name">Full name</Label
            ><Input
              id="driver-name"
              v-model="riderForm.name"
              maxlength="60"
              required
              placeholder="Driver name"
            />
          </div>
          <div class="space-y-2">
            <Label for="driver-phone">Phone number</Label
            ><Input
              id="driver-phone"
              v-model="riderForm.phone"
              type="tel"
              maxlength="20"
              required
              placeholder="+233 ..."
            />
          </div>
          <div class="space-y-2">
            <Label for="driver-vehicle">Vehicle / registration</Label
            ><Input
              id="driver-vehicle"
              v-model="riderForm.vehicle"
              maxlength="60"
              placeholder="Motorbike · GR 1234"
            />
          </div>
          <div class="space-y-2">
            <Label for="driver-service">Delivery service</Label
            ><Select v-model="riderForm.service"
              ><SelectTrigger id="driver-service"><SelectValue /></SelectTrigger
              ><SelectContent
                ><SelectItem
                  v-for="service in [
                    'in_house',
                    'yango',
                    'bolt',
                    'uber',
                    'other',
                  ]"
                  :key="service"
                  :value="service"
                  >{{ service.replace('_', ' ') }}</SelectItem
                ></SelectContent
              ></Select
            >
          </div>
          <div class="flex items-center gap-2 sm:col-span-2">
            <Switch id="driver-active" v-model="riderActive" /><Label
              for="driver-active"
              >Available for assignments</Label
            >
          </div>
          <p
            v-for="(error, field) in riderForm.errors"
            :key="field"
            class="text-sm text-destructive sm:col-span-2"
            role="alert"
          >
            {{ error }}
          </p>
          <div class="flex gap-2 sm:col-span-2">
            <Button type="submit" :disabled="riderForm.processing">{{
              editingRider ? 'Save driver' : 'Add driver'
            }}</Button
            ><Button
              v-if="editingRider"
              type="button"
              variant="ghost"
              @click="cancelRider"
              >Cancel</Button
            >
          </div>
        </form>
        <ul class="mt-3 divide-y">
          <li
            v-for="r in riders"
            :key="r.id"
            class="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm first:pt-0"
          >
            <span class="font-medium">{{ r.name }}</span>
            <span class="text-muted-foreground tabular-nums text-xs">{{
              r.phone
            }}</span>
            <span class="text-muted-foreground">{{
              r.vehicle ?? r.service
            }}</span>
            <StatusBadge
              v-if="!r.isActive"
              status="inactive"
              kind="delivery"
            /><Button
              size="xs"
              variant="outline"
              class="ml-auto"
              @click="editRider(r)"
              >Edit driver</Button
            >
          </li>
        </ul>
      </section>

      <!-- zones -->
      <section class="surface p-5">
        <h2 class="flex items-center gap-2 text-base font-semibold">
          <MapPin class="size-4" /> Zones
        </h2>
        <p class="mt-1 text-sm text-muted-foreground">
          Set delivery fees, customer ETAs and cash-on-delivery coverage.
        </p>
        <form
          class="mt-5 grid gap-4 rounded-xl border bg-muted/20 p-4 sm:grid-cols-2"
          @submit.prevent="saveZone"
        >
          <h3 class="font-medium sm:col-span-2">
            {{ editingZone ? 'Edit region' : 'Add delivery region' }}
          </h3>
          <div class="space-y-2">
            <Label for="region-name">Region name</Label
            ><Input
              id="region-name"
              v-model="zoneForm.name"
              maxlength="60"
              required
              placeholder="e.g. Greater Accra"
            />
          </div>
          <div class="space-y-2">
            <Label for="region-fee">Delivery fee</Label
            ><Input
              id="region-fee"
              v-model="zoneForm.fee"
              type="number"
              min="0"
              step="0.01"
              required
            />
          </div>
          <div class="space-y-2 sm:col-span-2">
            <Label for="region-eta">Estimated delivery</Label
            ><Input
              id="region-eta"
              v-model="zoneForm.etaText"
              maxlength="100"
              required
              placeholder="Same day for orders before 2pm"
            />
          </div>
          <div class="flex items-center gap-2 sm:col-span-2">
            <Switch
              id="region-cash"
              v-model="zoneForm.allowPayOnDelivery"
            /><Label for="region-cash">Allow cash on delivery</Label>
          </div>
          <div class="flex items-center gap-2 sm:col-span-2">
            <Switch id="region-active" v-model="zoneActive" /><Label
              for="region-active"
              >Region available at checkout</Label
            >
          </div>
          <p
            v-for="(error, field) in zoneForm.errors"
            :key="field"
            class="text-sm text-destructive sm:col-span-2"
            role="alert"
          >
            {{ error }}
          </p>
          <div class="flex gap-2 sm:col-span-2">
            <Button type="submit" :disabled="zoneForm.processing">{{
              editingZone ? 'Save region' : 'Add region'
            }}</Button
            ><Button
              v-if="editingZone"
              type="button"
              variant="ghost"
              @click="cancelZone"
              >Cancel</Button
            >
          </div>
        </form>
        <ul class="mt-3 divide-y">
          <li
            v-for="z in zones"
            :key="z.id"
            class="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm first:pt-0"
          >
            <span class="font-medium">{{ z.name }}</span>
            <span class="text-muted-foreground">{{ z.etaText }}</span>
            <span
              class="text-muted-foreground mr-auto text-xs"
              v-if="z.allowPayOnDelivery"
              >Cash on delivery</span
            >
            <span class="tabular-nums text-xs">{{ money(z.fee) }}</span>
            <Button size="xs" variant="outline" @click="editZone(z)"
              >Edit region</Button
            >
            <button
              class="text-muted-foreground hover:text-destructive text-xs"
              @click="toggleZone(z)"
            >
              {{ z.isActive ? 'deactivate' : 'reactivate' }}
            </button>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>
