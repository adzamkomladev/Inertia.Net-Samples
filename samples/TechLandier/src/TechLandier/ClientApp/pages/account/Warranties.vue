<script setup lang="ts">
import { ref } from 'vue'
import { Head, useForm } from '@inertiajs/vue3'
import { ShieldCheck, Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { fmtDate } from '@/lib/time'
import type { WarrantyView } from '@/types'

defineProps<{ warranties: WarrantyView[] }>()

const serialForm = useForm({ serialNumber: '' })
const claimForm = useForm({ issue: '' })
const openId = ref<string | null>(null)
</script>

<template>
  <Head title="My warranties" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="My warranties"
      description="Registered automatically when an order is delivered. Add the serial number and claim while the warranty is active."
    />

    <div v-if="warranties.length" class="flex flex-col gap-4">
      <div v-for="w in warranties" :key="w.id" class="surface p-5">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <h2 class="font-display text-base font-semibold">{{ w.itemName }}</h2>
          <StatusBadge :status="w.status" kind="warranty" />
          <span class="text-muted-foreground ml-auto tabular-nums text-xs">{{ fmtDate(w.startOn, false) }} → {{ fmtDate(w.endOn, false) }}</span>
        </div>

        <!-- serial -->
        <form
          class="border-border/50 mt-3 flex flex-wrap items-end gap-2 rounded-xl border p-3"
          @submit.prevent="serialForm.post(`/account/care/warranties/${w.id}/serial`)"
        >
          <div class="min-w-48 flex-1">
            <label class="text-muted-foreground text-xs font-medium" :for="`serial-${w.id}`">Device serial</label>
            <Input :id="`serial-${w.id}`" v-model="serialForm.serialNumber" class="mt-1 tabular-nums" placeholder="SN-…" maxlength="40" />
          </div>
          <Button type="submit" size="sm" variant="outline" :disabled="serialForm.processing">Save serial</Button>
        </form>

        <template v-if="w.status === 'active'">
          <Button size="sm" variant="outline" class="mt-3" @click="openId = openId === w.id ? null : w.id">
            <Wrench class="size-3.5" /> {{ openId === w.id ? 'Close' : 'Claim warranty' }}
          </Button>
          <form
            v-if="openId === w.id"
            class="mt-3 flex flex-col gap-2"
            @submit.prevent="claimForm.post(`/account/care/warranties/${w.id}/claims`, { onFinish: () => (claimForm.issue = '') })"
          >
            <Textarea v-model="claimForm.issue" rows="3" maxlength="800" required placeholder="What stopped working, and when did it start?" />
            <Button type="submit" size="sm" :disabled="claimForm.processing" class="w-fit">
              <ShieldCheck class="size-3.5" /> Submit claim
            </Button>
            <span v-if="claimForm.errors.issue" class="text-destructive text-xs">{{ claimForm.errors.issue }}</span>
          </form>
        </template>
      </div>
    </div>

    <EmptyState v-else :icon="ShieldCheck" title="No warranties" description="Orders for products with warranty months register their cover here the moment they're delivered." />
  </div>
</template>
