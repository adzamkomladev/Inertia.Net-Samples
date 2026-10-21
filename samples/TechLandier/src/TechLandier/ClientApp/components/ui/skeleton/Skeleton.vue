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
    :class="cn('animate-pulse rounded-md bg-primary/10 relative overflow-hidden after:pointer-events-none after:absolute after:inset-0 after:animate-shimmer after:bg-linear-to-r after:from-transparent after:via-white/45 after:to-transparent dark:after:via-white/10 motion-reduce:after:hidden', props.class)"
  />
</template>
