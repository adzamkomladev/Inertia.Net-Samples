<script setup lang="ts">
import { Skeleton } from '@/components/ui/skeleton'

withDefaults(defineProps<{ count?: number }>(), { count: 4 })
</script>

<!-- Placeholder with the same grid and card proportions as ProductCard. -->
<template>
  <div class="grid grid-cols-2 gap-x-4 gap-y-8 md:grid-cols-3 lg:grid-cols-4" aria-hidden="true">
    <div v-for="i in count" :key="i" class="space-y-3">
      <Skeleton class="aspect-square rounded-xl" />
      <Skeleton class="h-3 w-16" />
      <Skeleton class="h-4 w-3/4" />
      <Skeleton class="h-5 w-1/3" />
    </div>
  </div>
</template>
