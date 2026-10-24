<script setup lang="ts">
import { Head, Link, useForm } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import {
  Banknote, Bike, CheckCircle2, Clock, Loader2, MapPin, MoreHorizontal, PackageCheck, Pencil, Phone, Plus, Search,
  StickyNote, Truck, XCircle,
} from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import StatCard from '@/components/StatCard.vue'
import EmptyState from '@/components/EmptyState.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import AssignRider from '@/components/deliveries/AssignRider.vue'
import RiderLoad from '@/components/deliveries/RiderLoad.vue'
import RiderSheet from '@/components/deliveries/RiderSheet.vue'
import ZoneSheet from '@/components/deliveries/ZoneSheet.vue'
import DeliveryActionDialog, { type DeliveryAction } from '@/components/deliveries/DeliveryActionDialog.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { money, moneyWhole } from '@/lib/money'
import type { DeliveryRow, RiderAdminRow, ZoneAdminRow } from '@/types'

const props = defineProps<{
  deliveries: DeliveryRow[]
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
const count = (l: Lane) => props.deliveries.filter(lanes[l]).length
const lane = ref<string>(count('needs') > 0 ? 'needs' : 'active')

const query = ref('')
const zoneFilter = ref('all')
const limit = ref(30)

const matches = (d: DeliveryRow, q: string) =>
  !q || [d.orderNumber, d.customer, d.street, d.rider ?? '', d.zone ?? ''].some((v) => v.toLowerCase().includes(q))

const laneRows = computed(() => {
  const q = query.value.trim().toLowerCase()
  const rows = props.deliveries.filter((d) => lanes[lane.value as Lane](d) && matches(d, q) && (zoneFilter.value === 'all' || d.zoneId === zoneFilter.value))
  // Oldest waiting first while work is open; most recent first once it's done.
  return lane.value === 'done' ? rows.reverse() : rows
})
const visible = computed(() => laneRows.value.slice(0, limit.value))

const cashOut = computed(() =>
  props.deliveries.filter((d) => lanes.active(d) && d.paymentMethod === 'pay_on_delivery').reduce((s, d) => s + d.total, 0))
const deliveredToday = computed(() => {
  const today = new Date().toISOString().slice(0, 10)
  return props.deliveries.filter((d) => d.deliveredAt?.startsWith(today)).length
})
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
      <StatCard label="Needs a rider" :value="count('needs')" :icon="Clock" :tone="count('needs') ? 'warning' : 'default'" :hint="count('needs') ? 'Oldest first on the board' : 'Queue is clear'" />
      <StatCard label="On the road" :value="count('active')" :icon="Truck" :hint="`${spare} free slots · ${activeRiders.length} riders`" />
      <StatCard label="Cash with riders" :value="moneyWhole(cashOut)" :icon="Banknote" hint="Pay-on-delivery out now" />
      <StatCard label="Delivered today" :value="deliveredToday" :icon="PackageCheck" :hint="`${count('done')} in total`" />
    </section>

    <Tabs v-model="tab" class="gap-4">
      <TabsList class="h-10 w-full sm:w-fit">
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

        <TransitionGroup
          v-if="visible.length" tag="ul" class="relative divide-y"
          move-class="transition-transform duration-300"
          enter-active-class="transition duration-300 ease-(--ease-out-soft)" enter-from-class="opacity-0 -translate-y-1"
          leave-active-class="transition duration-200 absolute inset-x-0" leave-to-class="opacity-0 translate-x-4"
        >
          <li
            v-for="d in visible" :key="d.id"
            class="bg-card grid gap-x-4 gap-y-3 p-4 transition-opacity md:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)_minmax(0,1fr)_auto] md:items-center"
            :class="busy === d.id && 'pointer-events-none opacity-60'"
          >
            <!-- what & where -->
            <div class="min-w-0 space-y-1">
              <div class="flex flex-wrap items-center gap-2">
                <Link :href="`/admin/orders/${d.orderNumber}`" class="price text-sm font-semibold hover:underline">{{ d.orderNumber }}</Link>
                <StatusBadge :status="d.status" kind="delivery" />
                <span v-if="d.attempts" class="bg-destructive/10 text-destructive rounded-full px-2 py-0.5 text-[11px] font-medium">{{ d.attempts }} failed {{ d.attempts === 1 ? 'attempt' : 'attempts' }}</span>
              </div>
              <p class="truncate text-sm">{{ d.customer || 'Customer' }} <span class="text-muted-foreground">· {{ d.street || 'No street on file' }}</span></p>
              <p v-if="d.failedReason && d.status === 'failed'" class="text-destructive flex items-center gap-1.5 text-xs"><XCircle class="size-3.5 shrink-0" aria-hidden="true" />{{ d.failedReason }}</p>
              <button
                v-if="d.note" type="button" class="text-muted-foreground hover:text-foreground flex max-w-full items-center gap-1.5 text-left text-xs"
                @click="openDialog('note', d)"
              >
                <StickyNote class="size-3.5 shrink-0" aria-hidden="true" /><span class="truncate">{{ d.note }}</span>
              </button>
            </div>

            <!-- zone & money -->
            <div class="text-muted-foreground flex items-center gap-4 text-xs md:block md:space-y-1">
              <p class="flex items-center gap-1.5"><MapPin class="size-3.5" aria-hidden="true" />{{ d.zone ?? 'No zone' }}</p>
              <p v-if="d.cashCollected != null" class="text-success price font-medium">{{ money(d.cashCollected) }} collected</p>
              <p v-else-if="d.paymentMethod === 'pay_on_delivery'" class="text-foreground price font-medium">{{ money(d.total) }} to collect</p>
              <p v-else class="capitalize">{{ payLabel(d.paymentMethod) }} · paid</p>
            </div>

            <!-- rider & time in stage -->
            <div class="flex min-w-0 items-center justify-between gap-3 md:block md:space-y-0.5">
              <AssignRider v-if="d.status !== 'delivered'" :delivery="d" :riders="riders" />
              <p v-else class="flex items-center gap-1.5 truncate text-sm"><CheckCircle2 class="text-success size-4" aria-hidden="true" />{{ d.rider }}</p>
              <p class="text-muted-foreground text-xs">
                <template v-if="d.status === 'delivered'">{{ d.receivedBy ? `To ${d.receivedBy} · ` : '' }}</template>
                <template v-else-if="d.status === 'failed'">{{ d.rider ? `Last run by ${d.rider} · ` : 'Waiting ' }}</template>
                <template v-else-if="d.status === 'unassigned'">Waiting </template>
                <template v-else>{{ d.status === 'picked_up' ? 'Out since ' : 'Assigned ' }}</template>
                <RelativeTime v-if="stageSince(d)" :iso="stageSince(d)!" />
              </p>
            </div>

            <!-- next step -->
            <div class="flex items-center justify-end gap-1">
              <Button v-if="d.status === 'assigned'" size="sm" variant="outline" :disabled="busy === d.id" @click="pickup(d)">
                <Loader2 v-if="busy === d.id" class="animate-spin" aria-hidden="true" /><Bike v-else aria-hidden="true" /> Picked up
              </Button>
              <Button v-else-if="d.status === 'picked_up'" size="sm" @click="openDialog('deliver', d)"><PackageCheck aria-hidden="true" /> Delivered</Button>
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button size="icon-sm" variant="ghost" :aria-label="`More actions for ${d.orderNumber}`"><MoreHorizontal /></Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" class="w-48">
                  <DropdownMenuItem as-child><Link :href="`/admin/orders/${d.orderNumber}`"><Truck /> Open order</Link></DropdownMenuItem>
                  <DropdownMenuItem v-if="d.phone" as-child><a :href="`tel:${d.phone}`"><Phone /> Call customer</a></DropdownMenuItem>
                  <DropdownMenuItem @select="openDialog('note', d)"><StickyNote /> {{ d.note ? 'Edit note' : 'Add note' }}</DropdownMenuItem>
                  <template v-if="d.status === 'assigned' || d.status === 'picked_up'">
                    <DropdownMenuSeparator />
                    <DropdownMenuItem class="text-destructive focus:text-destructive" @select="openDialog('fail', d)"><XCircle class="text-destructive" /> Log failed attempt</DropdownMenuItem>
                  </template>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </li>
        </TransitionGroup>
        <EmptyState
          v-else :icon="lane === 'needs' && !query ? CheckCircle2 : Search"
          :title="query || zoneFilter !== 'all' ? 'No runs match' : lane === 'needs' ? 'Every order has a rider' : 'Nothing in this stage'"
          :description="query || zoneFilter !== 'all' ? 'Try another order number, customer or zone.' : 'Runs move here as orders are packed, assigned and completed.'"
        >
          <Button v-if="query || zoneFilter !== 'all'" variant="outline" size="sm" @click="query = ''; zoneFilter = 'all'">Clear filters</Button>
        </EmptyState>

        <div v-if="laneRows.length > limit" class="border-t p-3 text-center">
          <Button variant="ghost" size="sm" @click="limit += 30">Show {{ Math.min(30, laneRows.length - limit) }} more of {{ laneRows.length - limit }}</Button>
        </div>
      </TabsContent>

      <!-- riders -->
      <TabsContent value="riders">
        <ul v-if="riders.length" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <li
            v-for="r in riders" :key="r.id"
            class="surface group flex flex-col gap-4 p-4 transition-shadow hover:shadow-raised"
            :class="!r.isActive && 'opacity-70'"
          >
            <div class="flex items-start gap-3">
              <span class="bg-accent text-accent-foreground flex size-10 shrink-0 items-center justify-center rounded-full text-sm font-semibold">{{ initials(r.name) }}</span>
              <div class="min-w-0 flex-1">
                <p class="truncate font-medium">{{ r.name }}</p>
                <p class="text-muted-foreground truncate text-xs capitalize">{{ r.service.replace('_', ' ') }}<template v-if="r.vehicle"> · {{ r.vehicle }}</template></p>
              </div>
              <Button size="icon-sm" variant="ghost" :aria-label="`Edit ${r.name}`" @click="riderSheet = { open: true, rider: r }"><Pencil /></Button>
            </div>
            <div class="space-y-1.5">
              <div class="text-muted-foreground flex justify-between text-xs"><span>Current load</span><span>{{ r.completed }} delivered in 30 days</span></div>
              <RiderLoad :active="r.activeRuns" :capacity="r.capacity" />
            </div>
            <div class="flex items-center justify-between gap-2 border-t pt-3">
              <a :href="`tel:${r.phone}`" class="text-muted-foreground hover:text-foreground flex items-center gap-1.5 text-xs tabular-nums"><Phone class="size-3.5" aria-hidden="true" />{{ r.phone }}</a>
              <label class="flex items-center gap-2 text-xs">
                <Loader2 v-if="toggling === r.id" class="size-3.5 animate-spin" aria-hidden="true" />
                {{ r.isActive ? 'Available' : 'Off duty' }}
                <Switch :model-value="r.isActive" :disabled="toggling === r.id" :aria-label="`${r.name} available for assignments`" @update:model-value="(v: boolean) => toggle('riders', r.id, v)" />
              </label>
            </div>
          </li>
        </ul>
        <div v-else class="surface"><EmptyState :icon="Bike" title="No riders yet" description="Add your first rider to start dispatching orders."><Button @click="riderSheet = { open: true, rider: null }"><Plus /> Add rider</Button></EmptyState></div>
      </TabsContent>

      <!-- zones -->
      <TabsContent value="zones">
        <ul v-if="zones.length" class="surface divide-y overflow-hidden">
          <li v-for="z in zones" :key="z.id" class="flex flex-wrap items-center gap-x-4 gap-y-2 p-4" :class="!z.isActive && 'bg-muted/40'">
            <span class="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><MapPin class="size-4" aria-hidden="true" /></span>
            <div class="min-w-0 flex-1">
              <p class="flex items-center gap-2 font-medium">{{ z.name }} <StatusBadge v-if="!z.isActive" status="inactive" kind="rider" /></p>
              <p class="text-muted-foreground text-xs">{{ z.etaText }}<template v-if="z.allowPayOnDelivery"> · cash on delivery</template> · {{ zoneRuns(z) }} open {{ zoneRuns(z) === 1 ? 'run' : 'runs' }}</p>
            </div>
            <p class="price w-20 text-right font-medium">{{ money(z.fee) }}</p>
            <div class="flex items-center gap-1">
              <Switch :model-value="z.isActive" :disabled="toggling === z.id" :aria-label="`${z.name} offered at checkout`" @update:model-value="(v: boolean) => toggle('zones', z.id, v)" />
              <Button size="icon-sm" variant="ghost" :aria-label="`Edit ${z.name}`" @click="zoneSheet = { open: true, zone: z }"><Pencil /></Button>
            </div>
          </li>
        </ul>
        <div v-else class="surface"><EmptyState :icon="MapPin" title="No delivery zones" description="Customers need at least one zone to check out with delivery."><Button @click="zoneSheet = { open: true, zone: null }"><Plus /> Add zone</Button></EmptyState></div>
      </TabsContent>
    </Tabs>
  </div>

  <RiderSheet v-model:open="riderSheet.open" :rider="riderSheet.rider" />
  <ZoneSheet v-model:open="zoneSheet.open" :zone="zoneSheet.zone" />
  <DeliveryActionDialog v-model:open="dialog.open" :action="dialog.action" :delivery="dialog.delivery" />
</template>
