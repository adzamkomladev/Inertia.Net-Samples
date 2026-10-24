<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

interface SkeletonProps {
  class?: HTMLAttributes["class"]
}

const props = defineProps<SkeletonProps>()
</script>

<template>
  <!-- Pulsing base plus a sweeping highlight so placeholders read as "loading", not "broken". -->
  <div
    data-slot="skeleton"
    :class="cn('animate-pulse rounded-lg bg-muted', props.class)"
  />
</template>
