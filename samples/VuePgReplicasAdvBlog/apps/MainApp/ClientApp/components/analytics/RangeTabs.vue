<script setup lang="ts">
import { router } from '@inertiajs/vue3'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'

const props = defineProps<{ days: number; url: string }>()

const setDays = (d: string | number) =>
  router.get(props.url, { days: Number(d) }, { preserveState: true, preserveScroll: true, replace: true })
</script>

<template>
  <Tabs :model-value="String(days)" @update:model-value="setDays">
    <TabsList aria-label="Time range" class="h-auto rounded-lg bg-muted p-0.5">
      <TabsTrigger
        v-for="d in [7, 30, 90]"
        :key="d"
        :value="String(d)"
        class="rounded-md px-3 py-1 text-[13px] data-[state=active]:bg-background data-[state=active]:shadow-xs dark:data-[state=active]:border-transparent dark:data-[state=active]:bg-background"
      >{{ d }}d</TabsTrigger>
    </TabsList>
  </Tabs>
</template>