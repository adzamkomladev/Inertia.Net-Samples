<script setup lang="ts">
import { ref } from 'vue'
import { Link } from '@inertiajs/vue3'
import { ChevronLeft, ChevronRight } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { useScroll } from '@vueuse/core'

defineProps<{ title: string; href?: string; linkLabel?: string }>()

// Horizontally scrolling rail; arrows appear on pointer devices only.
const track = ref<HTMLElement | null>(null)
const { arrivedState } = useScroll(track)
const nudge = (dir: 1 | -1) => track.value?.scrollBy({ left: dir * track.value.clientWidth * 0.8, behavior: 'smooth' })
</script>

<template>
  <section class="mx-auto max-w-7xl px-4 pt-14 sm:px-6 lg:px-8">
    <div class="flex items-end justify-between gap-4">
      <h2 class="text-2xl font-semibold tracking-tight">{{ title }}</h2>
      <div class="flex items-center gap-1">
        <Link v-if="href" :href="href" prefetch class="text-primary mr-2 rounded-md text-sm font-medium hover:underline">{{ linkLabel ?? 'See all' }}</Link>
        <Button variant="secondary" size="icon-sm" class="hidden pointer-fine:inline-flex" aria-label="Scroll back" :disabled="arrivedState.left" @click="nudge(-1)"><ChevronLeft /></Button>
        <Button variant="secondary" size="icon-sm" class="hidden pointer-fine:inline-flex" aria-label="Scroll forward" :disabled="arrivedState.right" @click="nudge(1)"><ChevronRight /></Button>
      </div>
    </div>
    <div ref="track" class="scrollbar-none -mx-4 mt-5 flex snap-x scroll-px-4 gap-4 overflow-x-auto px-4 pb-2 sm:-mx-6 sm:scroll-px-6 sm:px-6 lg:mx-0 lg:scroll-px-0 lg:px-0">
      <slot />
    </div>
  </section>
</template>
