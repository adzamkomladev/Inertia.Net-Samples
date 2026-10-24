<script setup lang="ts">
import { computed } from 'vue'
import { categoryIcon } from '@/lib/icons'
import { cn } from '@/lib/utils'

const props = withDefaults(
  defineProps<{
    /** Deterministic seed (usually the product slug). */
    seed: string
    /** Lucide icon name stored on the category (Headphones, Cpu, ...). */
    icon?: string | null
    /** Brand name, printed small like a studio-shot label. */
    brand?: string | null
    /** Real image; when present it replaces the generated art. */
    imageUrl?: string | null
    alt?: string
    rounded?: string
  }>(),
  { icon: null, brand: null, imageUrl: null, alt: '', rounded: 'rounded-xl' },
)

const iconComponent = computed(() => categoryIcon(props.icon))

/** FNV-1a hash to a stable hue per slug. */
const hue = computed(() => {
  let h = 2166136261
  for (const c of props.seed) {
    h ^= c.charCodeAt(0)
    h = Math.imul(h, 16777619)
  }
  return Math.abs(h) % 360
})
</script>

<!-- Product photo, or a quiet studio backdrop (tinted by slug) with the category glyph when no photo exists. -->
<template>
  <img
    v-if="imageUrl"
    :src="imageUrl"
    :alt="alt"
    loading="lazy"
    :class="cn('bg-muted aspect-square h-full w-full object-cover', rounded)"
  />
  <div
    v-else
    aria-hidden="true"
    :class="cn('art relative flex aspect-square h-full w-full items-center justify-center overflow-hidden', rounded)"
    :style="{ '--h': hue }"
  >
    <component :is="iconComponent" class="size-[34%] drop-shadow-sm" :stroke-width="1.1" />
    <span class="absolute bottom-[17%] h-[3%] w-[30%] rounded-[50%] bg-current opacity-10 blur-[6px]" />
    <span v-if="brand" class="absolute bottom-[6%] text-[10px] font-semibold tracking-[0.18em] uppercase opacity-60">
      {{ brand }}
    </span>
  </div>
</template>
