<script setup lang="ts">
import { Skeleton } from '@/components/ui/skeleton'
import ProductGridSkeleton from '@/components/ProductGridSkeleton.vue'
import type { PendingKind } from '@/composables/usePendingVisit'

defineProps<{ kind: PendingKind }>()
</script>

<!-- The shape of the page a visitor is heading to, so a tap answers on the click and the old page never sits there looking stuck. -->
<template>
  <div role="status" aria-label="Loading page" class="animate-in fade-in duration-100">
    <div v-if="kind === 'grid'" class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
      <Skeleton class="h-9 w-56" />
      <Skeleton class="mt-3 h-4 w-72 max-w-full" />
      <Skeleton class="mt-6 h-11 w-full rounded-full" />
      <ProductGridSkeleton :count="8" class="mt-8" />
    </div>

    <div v-else-if="kind === 'product'" class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <Skeleton class="h-4 w-48" />
      <div class="mt-5 grid gap-x-12 gap-y-8 lg:grid-cols-[minmax(0,1fr)_25rem]">
        <Skeleton class="aspect-square rounded-xl sm:aspect-[4/3]" />
        <div class="surface space-y-4 p-5">
          <Skeleton class="h-4 w-20" /><Skeleton class="h-8 w-4/5" /><Skeleton class="h-8 w-32" />
          <Skeleton class="h-14 w-full rounded-lg" /><Skeleton class="h-14 w-full rounded-lg" /><Skeleton class="h-11 w-full rounded-lg" />
        </div>
      </div>
    </div>

    <div v-else-if="kind === 'checkout'" class="mx-auto max-w-6xl px-4 py-8 sm:px-6 lg:px-8">
      <Skeleton class="h-8 w-40" />
      <div class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_23rem]">
        <div class="space-y-6"><Skeleton class="h-56 rounded-xl" /><Skeleton class="h-64 rounded-xl" /></div>
        <Skeleton class="h-72 rounded-xl" />
      </div>
    </div>

    <div v-else-if="kind === 'dashboard'" class="space-y-6">
      <div class="space-y-2"><Skeleton class="h-8 w-48" /><Skeleton class="h-4 w-72 max-w-full" /></div>
      <div class="grid grid-cols-2 gap-3 lg:grid-cols-4"><Skeleton v-for="i in 4" :key="i" class="h-24 rounded-xl" /></div>
      <Skeleton class="h-64 rounded-xl" />
    </div>

    <div v-else class="space-y-6">
      <div class="space-y-2"><Skeleton class="h-8 w-48" /><Skeleton class="h-4 w-72 max-w-full" /></div>
      <div class="surface divide-y">
        <div v-for="i in 6" :key="i" class="space-y-2 p-4"><Skeleton class="h-4 w-1/3" /><Skeleton class="h-3 w-2/3" /></div>
      </div>
    </div>
  </div>
</template>
