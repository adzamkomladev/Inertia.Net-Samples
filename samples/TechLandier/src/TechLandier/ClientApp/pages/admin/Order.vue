<script setup lang="ts">
import { Head, Link, router, useForm } from '@inertiajs/vue3'
import { ref } from 'vue'
import { ArrowLeft, Ban, CheckCheck, CreditCard, Receipt, Save, Truck, User } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import PriceTag from '@/components/PriceTag.vue'
import TimelineList from '@/components/TimelineList.vue'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { money } from '@/lib/money'
import type { AdminCustomer, AdminRiderRow, DeliveryView, OrderEventRow, OrderView, PaymentRow, PlanView, WarrantyView, InsuranceView } from '@/types'

const props = defineProps<{
  order: OrderView
  events: OrderEventRow[]
  delivery: DeliveryView | null
  customer: AdminCustomer
  payments: PaymentRow[]
  plan: PlanView | null
  activeRiders: AdminRiderRow[]
  warranties?: WarrantyView[]
  insurances?: InsuranceView[]
}>()

const moves = ['paid', 'processing', 'shipped', 'delivered', 'cancelled']
const order = props.order
const next = ref(props.order.paymentStatus === 'paid' ? 'processing' : moves[Math.min(moves.length - 1, moves.indexOf(props.order.status) + 1)])
const statusForm = useForm({ to: '', note: '' })
const refundForm = useForm({ note: '' })
const busy = ref<string | null>(null)

const move = (to: string) => {
  busy.value = 'move'
  statusForm.to = to
  statusForm.post(`/admin/orders/${order.number}/status`, { onFinish: () => (busy.value = null) })
}
const refund = () => {
  busy.value = 'refund'
  refundForm.post(`/admin/orders/${order.number}/refund`, { onFinish: () => (busy.value = null) })
}
const settle = (id: string) => {
  busy.value = id
  router.post(`/admin/orders/payments/${id}/settle`, {}, { onFinish: () => (busy.value = null) })
}
</script>

<template>
  <Head :title="`Order ${order.number}`" />

  <div class="flex flex-col gap-6">
    <Link href="/admin/orders" class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1.5 text-sm">
      <ArrowLeft class="size-4" /> All orders
    </Link>

    <PageHeader :title="`Order ${order.number}`" :description="`Placed ${new Date(order.createdAt).toLocaleString()} · ${order.items.length} item(s)`">
      <template #actions>
        <StatusBadge :status="order.status" kind="order" />
        <StatusBadge :status="order.paymentStatus" kind="payment" />
      </template>
    </PageHeader>

    <div class="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <div class="flex flex-col gap-5">
        <!-- items -->
        <section class="surface p-5">
          <h2 class="font-display text-base font-semibold">Items & totals</h2>
          <ul class="mt-3 divide-y">
            <li v-for="item in order.items" :key="item.id" class="flex items-center justify-between gap-3 py-2 text-sm first:pt-0">
              <span class="truncate font-medium">{{ item.name }}</span>
              <span class="text-muted-foreground tabular-nums text-xs">x{{ item.qty }} @ {{ money(item.unitPrice) }}</span>
              <PriceTag :price="item.lineTotal" size="sm" />
            </li>
          </ul>
          <div class="mt-2 flex justify-between border-t pt-2 text-sm font-semibold">
            <span>Total</span><PriceTag :price="order.total" />
          </div>
        </section>

        <section class="surface p-5">
          <h2 class="font-display text-base font-semibold">Timeline</h2>
          <TimelineList :events="props.events" class="mt-3" />
        </section>

        <section v-if="plan" class="surface p-5">
          <h2 class="font-display text-base font-semibold">Payment plan (BNPL)</h2>
          <InstallmentPlanCard :plan="plan" :order-number="order.number" :show-pay="false" class="mt-3" />
        </section>
      </div>

      <!-- ops sidebar -->
      <aside class="flex flex-col gap-5">
        <!-- actions -->
        <section class="surface flex flex-col gap-3 p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Save class="size-4" /> Operate</h2>

          <form @submit.prevent="move(next)">
            <div class="grid gap-1.5">
              <Label for="a-next">Move status to</Label>
              <Select v-model="next">
                <SelectTrigger id="a-next"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="s in moves" :key="s" :value="s">{{ s }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Button type="submit" size="sm" class="mt-2 w-fit" :disabled="statusForm.processing"><CheckCheck class="size-3.5" /> Move</Button>
          </form>

          <form class="border-t pt-3" @submit.prevent="refund">
            <Label for="a-note">Refund note</Label>
            <Textarea id="a-note" v-model="refundForm.note" rows="2" class="mt-1" maxlength="200" placeholder="Reason — lands on the customer's record" />
            <Button type="submit" size="sm" variant="destructive" class="mt-2 w-fit" :disabled="refundForm.processing || order.paymentStatus !== 'paid'">
              <Ban class="size-3.5" /> Refund payment
            </Button>
          </form>
        </section>

        <!-- delivery / rider assignment -->
        <section class="surface p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Truck class="size-4" /> Delivery</h2>
          <div v-if="props.delivery" class="mt-3 space-y-2 text-sm">
            <div class="flex items-center justify-between"><span class="text-muted-foreground">Status</span><StatusBadge :status="props.delivery.status" kind="delivery" /></div>
            <div class="flex justify-between"><span class="text-muted-foreground">Zone</span><span>{{ props.delivery.zoneName ?? '—' }}</span></div>
            <div class="flex justify-between"><span class="text-muted-foreground">Rider</span><span>{{ props.delivery.riderName ?? 'unassigned' }}</span></div>
          </div>
          <p v-else class="text-muted-foreground mt-2 text-sm">No delivery row yet — assign one once the order is confirmed for dispatch.</p>
        </section>

        <!-- payment rows -->
        <section class="surface p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><CreditCard class="size-4" /> Payments</h2>
          <ul v-if="payments.length" class="mt-3 space-y-2 text-sm">
            <li v-for="p in payments" :key="p.id" class="border-border/50 flex items-center gap-2 rounded-xl border p-2.5">
              <StatusBadge :status="p.status" kind="payment" />
              <span class="min-w-0 flex-1 truncate">
                {{ money(p.amount) }} <span class="text-muted-foreground">· {{ p.method.replace('_', ' ') }}</span>
              </span>
              <Receipt
                class="text-muted-foreground/70 size-4"
                aria-hidden="true"
              />
              <Button v-if="p.status !== 'paid'" size="xs" variant="outline" class="rounded-full" :disabled="busy === p.id" @click="settle(p.id)">
                Settle
              </Button>
            </li>
          </ul>
          <p v-else class="text-muted-foreground mt-2 text-sm">No payment rows.</p>
        </section>

        <!-- customer card -->
        <section class="surface p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><User class="size-4" /> Customer & address</h2>
          <div class="mt-2 space-y-1 text-sm">
            <p class="font-medium">{{ customer.name }}</p>
            <p class="text-muted-foreground">{{ customer.email ?? 'no email' }} · {{ customer.phone }}</p>
            <p class="text-muted-foreground">{{ customer.street }}</p>
            <p v-if="customer.notes" class="text-xs italic">“{{ customer.notes }}”</p>
          </div>
        </section>
      </aside>
    </div>
  </div>
</template>
