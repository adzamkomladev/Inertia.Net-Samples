<script setup lang="ts">
import { Head, router, useForm } from '@inertiajs/vue3'
import { ref } from 'vue'
import { BadgeCheck, FileWarning } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { InsuranceView } from '@/types'

defineProps<{
  policies: InsuranceView[]
  attachable: { orderItemId: string; itemName: string; product: string; price: number }[]
}>()

const claimForm = useForm({ incidentType: 'damage', description: '' })
const openId = ref<string | null>(null)

const fileClaim = (id: string) =>
  claimForm.post(`/account/care/insurance/${id}/claims`, { onSuccess: () => { claimForm.description = ''; openId.value = null } })

const attach = (orderItemId: string) => router.post(`/account/care/insurance/attach/${orderItemId}`, {}, { preserveScroll: true })
</script>

<template>
  <Head title="Device insurance" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="Device insurance"
      description="A policy costs 15% of the device price and covers 12 months of damage, theft or malfunction. An approved claim pays out 90% of the price to your mobile money."
    />

    <div v-if="policies.length" class="flex flex-col gap-4">
      <div v-for="p in policies" :key="p.id" class="surface p-5">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <h2 class="font-display text-base font-semibold">{{ p.deviceName }}</h2>
          <StatusBadge :status="p.status" kind="insurance" />
          <span class="text-muted-foreground ml-auto tabular-nums text-xs">{{ fmtDate(p.startOn, true) }} → {{ fmtDate(p.endOn, true) }}</span>
        </div>
        <p class="text-muted-foreground mt-1 tabular-nums text-xs">{{ p.deviceSerial || 'serial not set' }} · {{ money(p.premium) }}/yr · covers {{ money(p.coverageAmount) }}</p>
        <template v-if="p.status === 'active'">
          <Button size="sm" variant="outline" class="mt-3" @click="openId = openId === p.id ? null : p.id">
            <FileWarning class="size-3.5" /> {{ openId === p.id ? 'Close' : 'File a claim' }}
          </Button>
          <form
            v-if="openId === p.id"
            class="mt-3 flex flex-col gap-3"
            @submit.prevent="fileClaim(p.id)"
          >
            <Select v-model="claimForm.incidentType">
              <SelectTrigger class="w-48"><SelectValue placeholder="Incident type" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="damage">Damage</SelectItem>
                <SelectItem value="theft">Theft</SelectItem>
                <SelectItem value="malfunction">Malfunction</SelectItem>
              </SelectContent>
            </Select>
            <Textarea v-model="claimForm.description" rows="3" maxlength="800" required placeholder="What happened, when and where — the more specific the claim, the faster the review." />
            <Button type="submit" size="sm" :disabled="claimForm.processing" class="w-fit">
              <BadgeCheck class="size-3.5" /> Submit claim
            </Button>
            <span v-if="claimForm.errors.description" class="text-destructive text-xs">{{ claimForm.errors.description }}</span>
          </form>
        </template>
      </div>
    </div>

    <EmptyState v-else :icon="BadgeCheck" title="No policies" description="Attach cover to an eligible delivered device below, or tick it at checkout." />

    <section v-if="attachable.length" class="surface p-5">
      <h2 class="font-display text-base font-semibold">Attach cover to a delivered device</h2>
      <ul class="mt-3 divide-y">
        <li v-for="a in attachable" :key="a.orderItemId" class="flex flex-wrap items-center gap-3 py-3 first:pt-0">
          <span class="min-w-0 flex-1 truncate text-sm font-medium">{{ a.itemName }}</span>
          <span class="text-muted-foreground tabular-nums text-xs">device {{ money(a.price * 0.15) }}/yr</span>
          <Button size="sm" variant="outline" class="rounded-full" @click="attach(a.orderItemId)">Insure this device</Button>
        </li>
      </ul>
    </section>
  </div>
</template>
