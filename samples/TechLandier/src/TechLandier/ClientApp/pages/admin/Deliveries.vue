<script setup lang="ts">
import { Head, Link, router, useForm } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import {
  Banknote, Bike, CheckCircle2, Clock, Loader2, MapPin, PackageCheck, Pencil, Phone, Plus, Search,
  StickyNote, Truck, XCircle,
} from '@lucide/vue'
import DataTable from '@/components/DataTable.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import StatCard from '@/components/StatCard.vue'
import EmptyState from '@/components/EmptyState.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import AssignRider from '@/components/deliveries/AssignRider.vue'
import RunMenu from '@/components/deliveries/RunMenu.vue'
import RiderLoad from '@/components/deliveries/RiderLoad.vue'
import RiderSheet from '@/components/deliveries/RiderSheet.vue'
import ZoneSheet from '@/components/deliveries/ZoneSheet.vue'
import DeliveryActionDialog, { type DeliveryAction } from '@/components/deliveries/DeliveryActionDialog.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { money, moneyWhole } from '@/lib/money'
import type { DeliveryRow, NumberedPage, RiderAdminRow, ZoneAdminRow } from '@/types'

// deliveries: every run still open. delivered: completed runs, one page at a time (?page=).
const props = defineProps<{
  deliveries: DeliveryRow[]
  delivered: NumberedPage<DeliveryRow>
  deliveredToday: number
  riders: RiderAdminRow[]
  zones: ZoneAdminRow[]
}>()

// ---------------------------------------------------------------- board

const lanes = {
  needs: (d: DeliveryRow) => d.status === 'unassigned' || d.status === 'failed',
  active: (d: DeliveryRow) => d.status === 'assigned' || d.status === 'picked_up',
  done: (d: DeliveryRow) => d.status === 'delivered',
}
type Lane = keyof typeof lanes
const count = (l: Lane) => (l === 'done' ? (props.delivered.total ?? props.delivered.data.length) : props.deliveries.filter(lanes[l]).length)
const everyRun = computed(() => [...props.deliveries, ...props.delivered.data])

const historyLoading = ref(false)
function historyPage(page: number) {
  router.get('/admin/deliveries', page > 1 ? { page } : {}, {
    only: ['delivered'], preserveState: true, preserveScroll: true, replace: true,
    onStart: () => (historyLoading.value = true), onFinish: () => (historyLoading.value = false),
  })
}
const lane = ref<string>(count('needs') > 0 ? 'needs' : 'active')

const query = ref('')
const zoneFilter = ref('all')
const limit = ref(30)

const matches = (d: DeliveryRow, q: string) =>
  !q || [d.orderNumber, d.customer, d.street, d.rider ?? '', d.zone ?? ''].some((v) => v.toLowerCase().includes(q))

const laneRows = computed(() => {
  const q = query.value.trim().toLowerCase()
  const rows = everyRun.value.filter((d) => lanes[lane.value as Lane](d) && matches(d, q) && (zoneFilter.value === 'all' || d.zoneId === zoneFilter.value))
  // Oldest waiting first while work is open; completed runs arrive newest first from the server.
  return rows
})
const visible = computed(() => laneRows.value.slice(0, limit.value))

const cashOut = computed(() =>
  props.deliveries.filter((d) => lanes.active(d) && d.paymentMethod === 'pay_on_delivery').reduce((s, d) => s + d.total, 0))
const activeRiders = computed(() => props.riders.filter((r) => r.isActive))
const spare = computed(() => activeRiders.value.reduce((s, r) => s + Math.max(0, r.capacity - r.activeRuns), 0))

/** The timestamp that says how long a run has sat in its current stage. */
const stageSince = (d: DeliveryRow) => (d.status === 'picked_up' ? d.pickedUpAt : d.status === 'assigned' ? d.assignedAt : d.status === 'delivered' ? d.deliveredAt : d.createdAt)
const payLabel = (method: string) => method.replace(/_/g, ' ')

// One inline action at a time per row: the button shows a spinner and the row dims.
const busy = ref<string | null>(null)
const pickupForm = useForm({})
function pickup(d: DeliveryRow) {
  busy.value = d.id
  pickupForm.post(`/admin/deliveries/${d.id}/pickup`, { preserveScroll: true, onFinish: () => (busy.value = null) })
}

const dialog = ref<{ open: boolean; action: DeliveryAction; delivery: DeliveryRow | null }>({ open: false, action: 'deliver', delivery: null })
const openDialog = (action: DeliveryAction, delivery: DeliveryRow) => (dialog.value = { open: true, action, delivery })

// ---------------------------------------------------------------- riders & zones

const riderSheet = ref<{ open: boolean; rider: RiderAdminRow | null }>({ open: false, rider: null })
const zoneSheet = ref<{ open: boolean; zone: ZoneAdminRow | null }>({ open: false, zone: null })
const tab = ref('board')

// Availability switches post a partial update ({ isActive }); the row shows a spinner while it saves.
const toggling = ref<string | null>(null)
const toggleForm = useForm({ isActive: true })
function toggle(kind: 'riders' | 'zones', id: string, isActive: boolean) {
  toggling.value = id
  toggleForm.isActive = isActive
  toggleForm.post(`/admin/${kind}/${id}`, { preserveScroll: true, onFinish: () => (toggling.value = null) })
}
const initials = (name: string) => name.split(' ').map((p) => p[0]).join('').slice(0, 2)
const zoneRuns = (z: ZoneAdminRow) => props.deliveries.filter((d) => d.zoneId === z.id && !lanes.done(d)).length
</script>

<template>
  <Head title="Deliveries" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Deliveries" description="Dispatch riders, follow every run and reconcile cash on delivery.">
      <template #actions>
        <Button variant="outline" @click="zoneSheet = { open: true, zone: null }"><MapPin /> Add zone</Button>
        <Button @click="riderSheet = { open: true, rider: null }"><Plus /> Add rider</Button>
      </template>
    </PageHeader>

    <section aria-label="Dispatch summary" class="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <StatCard label="To dispatch" :value="count('needs')" :icon="Clock" :tone="count('needs') ? 'warning' : 'default'" :hint="count('needs') ? 'Oldest first on the board' : 'Queue is clear'" />
      <StatCard label="On the road" :value="count('active')" :icon="Truck" :hint="`${spare} free slots · ${activeRiders.length} riders`" />
      <StatCard label="Cash with riders" :value="moneyWhole(cashOut)" :icon="Banknote" hint="Pay-on-delivery out now" />
      <StatCard label="Delivered today" :value="props.deliveredToday":icon="PackageCheck" :hint="`${count('done')} in total`" />
    </section>

    <Tabs v-model="tab" class="gap-4">
      <TabsList class="h-10 w-full sm:w-fit pointer-coarse:h-14">
        <TabsTrigger value="board" class="px-4">Board</TabsTrigger>
        <TabsTrigger value="riders" class="px-4">Riders <span class="text-muted-foreground tabular-nums">{{ riders.length }}</span></TabsTrigger>
        <TabsTrigger value="zones" class="px-4">Zones <span class="text-muted-foreground tabular-nums">{{ zones.length }}</span></TabsTrigger>
      </TabsList>

      <!-- board -->
      <TabsContent value="board" class="surface overflow-hidden">
        <div class="flex flex-col gap-3 border-b p-3 lg:flex-row lg:items-center">
          <SegmentedControl
            v-model="lane" label="Delivery stage" class="scrollbar-none max-w-full overflow-x-auto"
            :options="[
              { value: 'needs', label: `To dispatch · ${count('needs')}` },
              { value: 'active', label: `On the road · ${count('active')}` },
              { value: 'done', label: `Delivered · ${count('done')}` },
            ]"
            @update:model-value="limit = 30"
          />
          <div class="flex gap-2 lg:ml-auto">
            <div class="relative min-w-0 flex-1 lg:w-64">
              <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
              <Input v-model="query" type="search" placeholder="Order, customer, area…" class="pl-9" aria-label="Search deliveries" />
            </div>
            <Select v-model="zoneFilter">
              <SelectTrigger class="w-36 shrink-0" aria-label="Filter by zone"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All zones</SelectItem>
                <SelectItem v-for="z in zones" :key="z.id" :value="z.id">{{ z.name }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <DataTable
          v-if="visible.length" bare row-key="id" :rows="visible" :row-class="(d: any) => busy === d.id && 'pointer-events-none opacity-60'"
          :columns="[
            { key: 'order', label: 'Order', primary: true },
            { key: 'zone', label: 'Zone' },
            { key: 'rider', label: 'Rider', full: true },
            { key: 'next', label: 'Next step', align: 'right' as const, hideLabel: true, full: true },
          ]"
        >
          <template #cell-order="{ row: d }">
            <span class="block min-w-0 space-y-1">
              <span class="flex flex-wrap items-center gap-2">
                <Link :href="`/admin/orders/${d.orderNumber}`" class="price -my-1 flex items-center py-1 text-sm font-semibold hover:underline pointer-coarse:min-h-11">{{ d.orderNumber }}</Link>
                <StatusBadge :status="d.status" kind="delivery" />
                <span v-if="d.attempts" class="bg-destructive/10 text-destructive rounded-full px-2 py-0.5 text-[11px] font-medium">{{ d.attempts }} failed {{ d.attempts === 1 ? 'attempt' : 'attempts' }}</span>
              </span>
              <span class="block truncate text-sm font-normal">{{ d.customer || 'Customer' }} <span class="text-muted-foreground">· {{ d.street || 'No street on file' }}</span></span>
              <span v-if="d.failedReason && d.status === 'failed'" class="text-destructive flex items-center gap-1.5 text-xs font-normal"><XCircle class="size-3.5 shrink-0" aria-hidden="true" />{{ d.failedReason }}</span>
              <button
                v-if="d.note" type="button" class="text-muted-foreground hover:text-foreground flex max-w-full items-center gap-1.5 py-1 text-left text-xs font-normal"
                @click="openDialog('note', d)"
              >
                <StickyNote class="size-3.5 shrink-0" aria-hidden="true" /><span class="truncate">{{ d.note }}</span>
              </button>
            </span>
          </template>
          <template #cell-zone="{ row: d }">
            <span class="text-muted-foreground block space-y-1 text-xs">
              <span class="flex items-center gap-1.5"><MapPin class="size-3.5" aria-hidden="true" />{{ d.zone ?? 'No zone' }}</span>
              <span v-if="d.cashCollected != null" class="text-success price block font-medium">{{ money(d.cashCollected) }} collected</span>
              <span v-else-if="d.paymentMethod === 'pay_on_delivery'" class="text-foreground price block font-medium">{{ money(d.total) }} to collect</span>
              <span v-else class="block capitalize">{{ payLabel(d.paymentMethod) }} · paid</span>
            </span>
          </template>
          <template #cell-rider="{ row: d }">
            <span class="block min-w-0 space-y-0.5">
              <AssignRider v-if="d.status !== 'delivered'" :delivery="d" :riders="riders" />
              <span v-else class="flex items-center gap-1.5 truncate text-sm"><CheckCircle2 class="text-success size-4" aria-hidden="true" />{{ d.rider }}</span>
              <span class="text-muted-foreground block text-xs">
                <template v-if="d.status === 'delivered'">{{ d.receivedBy ? `To ${d.receivedBy} · ` : '' }}</template>
                <template v-else-if="d.status === 'failed'">{{ d.rider ? `Last run by ${d.rider} · ` : 'Waiting ' }}</template>
                <template v-else-if="d.status === 'unassigned'">Waiting </template>
                <template v-else>{{ d.status === 'picked_up' ? 'Out since ' : 'Assigned ' }}</template>
                <RelativeTime v-if="stageSince(d)" :iso="stageSince(d)!" />
              </span>
            </span>
          </template>
          <template #cell-next="{ row: d }">
            <span class="flex items-center justify-end gap-1">
              <Button v-if="d.status === 'assigned'" size="sm" variant="outline" class="pointer-coarse:h-11" :disabled="busy === d.id" @click="pickup(d)">
                <Loader2 v-if="busy === d.id" class="animate-spin" aria-hidden="true" /><Bike v-else aria-hidden="true" /> Picked up
              </Button>
              <Button v-else-if="d.status === 'picked_up'" size="sm" class="pointer-coarse:h-11" @click="openDialog('deliver', d)"><PackageCheck aria-hidden="true" /> Delivered</Button>
              <RunMenu :delivery="d" @note="openDialog('note', d)" @fail="openDialog('fail', d)" />
            </span>
          </template>
          <!-- phone card: order, status and the menu on one line; rider and next step share the last -->
          <template #card="{ row: d }">
            <div class="space-y-2">
              <div class="flex items-center gap-2">
                <Link :href="`/admin/orders/${d.orderNumber}`" class="price flex min-h-11 min-w-0 items-center text-sm font-semibold hover:underline">{{ d.orderNumber }}</Link>
                <StatusBadge :status="d.status" kind="delivery" />
                <span v-if="d.attempts" class="bg-destructive/10 text-destructive rounded-full px-2 py-0.5 text-[11px] font-medium">{{ d.attempts }} failed</span>
                <span class="-my-1 -mr-2 ml-auto"><RunMenu :delivery="d" @note="openDialog('note', d)" @fail="openDialog('fail', d)" /></span>
              </div>
              <p class="truncate text-sm">{{ d.customer || 'Customer' }} <span class="text-muted-foreground">· {{ d.street || 'No street on file' }}</span></p>
              <p v-if="d.failedReason && d.status === 'failed'" class="text-destructive flex items-center gap-1.5 text-xs"><XCircle class="size-3.5 shrink-0" aria-hidden="true" />{{ d.failedReason }}</p>
              <button v-if="d.note" type="button" class="text-muted-foreground flex min-h-11 max-w-full items-center gap-1.5 text-left text-xs" @click="openDialog('note', d)">
                <StickyNote class="size-3.5 shrink-0" aria-hidden="true" /><span class="truncate">{{ d.note }}</span>
              </button>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="min-w-0">
                  <AssignRider v-if="d.status !== 'delivered'" :delivery="d" :riders="riders" />
                  <span v-else class="flex items-center gap-1.5 truncate text-sm"><CheckCircle2 class="text-success size-4" aria-hidden="true" />{{ d.rider }}</span>
                </div>
                <Button v-if="d.status === 'assigned'" size="sm" variant="outline" class="pointer-coarse:h-11" :disabled="busy === d.id" @click="pickup(d)">
                  <Loader2 v-if="busy === d.id" class="animate-spin" aria-hidden="true" /><Bike v-else aria-hidden="true" /> Picked up
                </Button>
                <Button v-else-if="d.status === 'picked_up'" size="sm" class="pointer-coarse:h-11" @click="openDialog('deliver', d)"><PackageCheck aria-hidden="true" /> Delivered</Button>
              </div>
              <p class="text-muted-foreground text-xs">
                {{ d.zone ?? 'No zone' }} ·
                <span v-if="d.cashCollected != null" class="text-success price font-medium">{{ money(d.cashCollected) }} collected</span>
                <span v-else-if="d.paymentMethod === 'pay_on_delivery'" class="text-foreground price font-medium">{{ money(d.total) }} to collect</span>
                <span v-else class="capitalize">{{ payLabel(d.paymentMethod) }} · paid</span>
                · <template v-if="d.status === 'delivered'">delivered </template><template v-else-if="d.status === 'picked_up'">out since </template><template v-else-if="d.status === 'assigned'">assigned </template><template v-else>waiting </template><RelativeTime v-if="stageSince(d)" :iso="stageSince(d)!" />
              </p>
            </div>
          </template>
        </DataTable>
        <EmptyState
          v-else :icon="lane === 'needs' && !query ? CheckCircle2 : Search"
          :title="query || zoneFilter !== 'all' ? 'No runs match' : lane === 'needs' ? 'Every order has a rider' : 'Nothing in this stage'"
          :description="query || zoneFilter !== 'all' ? 'Try another order number, customer or zone.' : 'Runs move here as orders are packed, assigned and completed.'"
        >
          <Button v-if="query || zoneFilter !== 'all'" variant="outline" size="sm" class="pointer-coarse:h-11" @click="query = ''; zoneFilter = 'all'">Clear filters</Button>
        </EmptyState>

        <nav
          v-if="lane === 'done' && (delivered.currentPage > 1 || delivered.hasMore)" class="flex items-center justify-between gap-3 border-t p-3"
          aria-label="Delivered runs pages"
        >
          <Button variant="ghost" size="sm" class="pointer-coarse:h-11" :disabled="delivered.currentPage <= 1 || historyLoading" @click="historyPage(delivered.currentPage - 1)">Newer</Button>
          <span class="text-muted-foreground text-xs tabular-nums">Page {{ delivered.currentPage }}<template v-if="delivered.lastPage"> of {{ delivered.lastPage }}</template></span>
          <Button variant="ghost" size="sm" class="pointer-coarse:h-11" :disabled="!delivered.hasMore || historyLoading" @click="historyPage(delivered.currentPage + 1)">Older</Button>
        </nav>

        <div v-if="laneRows.length > limit" class="border-t p-3 text-center">
          <Button variant="ghost" size="sm" class="pointer-coarse:h-11" @click="limit += 30">Show {{ Math.min(30, laneRows.length - limit) }} more of {{ laneRows.length - limit }}</Button>
        </div>
      </TabsContent>

      <!-- riders -->
      <TabsContent value="riders">
        <DataTable
          v-if="riders.length" row-key="id" :rows="riders" :row-class="(r: any) => !r.isActive && 'opacity-70'"
          :columns="[
            { key: 'name', label: 'Rider', primary: true },
            { key: 'load', label: 'Current load' },
            { key: 'phone', label: 'Phone' },
            { key: 'available', label: 'Availability', align: 'right' as const },
            { key: 'edit', label: 'Edit', align: 'right' as const, hideLabel: true },
          ]"
        >
          <template #cell-name="{ row: r }">
            <span class="flex min-w-0 items-center gap-3">
              <span class="bg-accent text-accent-foreground flex size-10 shrink-0 items-center justify-center rounded-full text-sm font-semibold">{{ initials(r.name) }}</span>
              <span class="min-w-0">
                <span class="block truncate font-medium">{{ r.name }}</span>
                <span class="text-muted-foreground block truncate text-xs font-normal capitalize">{{ r.service.replace('_', ' ') }}<template v-if="r.vehicle"> · {{ r.vehicle }}</template></span>
              </span>
            </span>
          </template>
          <template #cell-load="{ row: r }">
            <span class="block min-w-32 space-y-1.5">
              <RiderLoad :active="r.activeRuns" :capacity="r.capacity" />
              <span class="text-muted-foreground block text-xs">{{ r.completed }} delivered in 30 days</span>
            </span>
          </template>
          <template #cell-phone="{ row: r }">
            <a :href="`tel:${r.phone}`" class="text-muted-foreground hover:text-foreground flex min-h-8 items-center gap-1.5 text-xs tabular-nums"><Phone class="size-3.5" aria-hidden="true" />{{ r.phone }}</a>
          </template>
          <template #cell-available="{ row: r }">
            <label class="flex items-center gap-2 text-xs md:justify-end">
              <Loader2 v-if="toggling === r.id" class="size-3.5 animate-spin" aria-hidden="true" />
              {{ r.isActive ? 'Available' : 'Off duty' }}
              <Switch :model-value="r.isActive" :disabled="toggling === r.id" :aria-label="`${r.name} available for assignments`" @update:model-value="(v: boolean) => toggle('riders', r.id, v)" />
            </label>
          </template>
          <template #cell-edit="{ row: r }">
            <Button size="icon-sm" variant="ghost" class="pointer-coarse:size-11" :aria-label="`Edit ${r.name}`" @click="riderSheet = { open: true, rider: r }"><Pencil /></Button>
          </template>
        </DataTable>
        <div v-else class="surface"><EmptyState :icon="Bike" title="No riders yet" description="Add your first rider to start dispatching orders."><Button @click="riderSheet = { open: true, rider: null }"><Plus /> Add rider</Button></EmptyState></div>
      </TabsContent>

      <!-- zones -->
      <TabsContent value="zones">
        <DataTable
          v-if="zones.length" row-key="id" :rows="zones" :row-class="(z: any) => !z.isActive && 'bg-muted/40'"
          :columns="[
            { key: 'name', label: 'Zone', primary: true },
            { key: 'fee', label: 'Fee', align: 'right' as const },
            { key: 'offered', label: 'Offered at checkout', align: 'right' as const },
            { key: 'edit', label: 'Edit', align: 'right' as const, hideLabel: true },
          ]"
        >
          <template #cell-name="{ row: z }">
            <span class="flex min-w-0 items-center gap-3">
              <span class="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><MapPin class="size-4" aria-hidden="true" /></span>
              <span class="min-w-0">
                <span class="flex items-center gap-2 font-medium">{{ z.name }} <StatusBadge v-if="!z.isActive" status="inactive" kind="rider" /></span>
                <span class="text-muted-foreground block text-xs font-normal">{{ z.etaText }}<template v-if="z.allowPayOnDelivery"> · cash on delivery</template> · {{ zoneRuns(z) }} open {{ zoneRuns(z) === 1 ? 'run' : 'runs' }}</span>
              </span>
            </span>
          </template>
          <template #cell-fee="{ row: z }"><span class="price font-medium">{{ money(z.fee) }}</span></template>
          <template #cell-offered="{ row: z }">
            <span class="flex md:justify-end"><Switch :model-value="z.isActive" :disabled="toggling === z.id" :aria-label="`${z.name} offered at checkout`" @update:model-value="(v: boolean) => toggle('zones', z.id, v)" /></span>
          </template>
          <template #cell-edit="{ row: z }"><Button size="icon-sm" variant="ghost" class="pointer-coarse:size-11" :aria-label="`Edit ${z.name}`" @click="zoneSheet = { open: true, zone: z }"><Pencil /></Button></template>
        </DataTable>
        <div v-else class="surface"><EmptyState :icon="MapPin" title="No delivery zones" description="Customers need at least one zone to check out with delivery."><Button @click="zoneSheet = { open: true, zone: null }"><Plus /> Add zone</Button></EmptyState></div>
      </TabsContent>
    </Tabs>
  </div>

  <RiderSheet v-model:open="riderSheet.open" :rider="riderSheet.rider" />
  <ZoneSheet v-model:open="zoneSheet.open" :zone="zoneSheet.zone" />
  <DeliveryActionDialog v-model:open="dialog.open" :action="dialog.action" :delivery="dialog.delivery" />
</template>
