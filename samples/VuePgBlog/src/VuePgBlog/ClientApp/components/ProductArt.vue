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
    /** Brand glyph rendered in the corner. */
    brand?: string | null
    /** Real S3 image, when present, overrides the generated art. */
    imageUrl?: string | null
    alt?: string
    rounded?: string
  }>(),
  { icon: null, brand: null, imageUrl: null, alt: '', rounded: 'rounded-2xl' },
)

const iconComponent = computed<Component>(() => categoryIcon(props.icon))

/** FNV-1a-ish hash → two oklch hues, stable per slug. */
const hue = (offset: number) => {
  let h = 2166136261
  for (const c of props.seed) {
    h ^= c.charCodeAt(0)
    h = Math.imul(h, 16777619)
  }
  return (Math.abs(h) + offset) % 360
}

const style = computed(() => {
  const h1 = hue(0)
  const h2 = hue(47)
  return {
    background: `linear-gradient(135deg, oklch(0.72 0.12 ${h1}) 0%, oklch(0.55 0.16 ${h2}) 55%, oklch(0.38 0.1 ${h2}) 100%)`,
  }
})

const initials = computed(() =>
  (props.brand ?? '')
    .split(/\s+/)
    .map((p) => p[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase(),
)
</script>

<!-- Deterministic product art: brand-safe gradient + category glyph. Real images win when present. -->
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
    :class="cn('relative flex aspect-square h-full w-full items-center justify-center overflow-hidden', rounded)"
    :style="style"
  >
    <component :is="iconComponent" class="size-[38%] text-white/90 drop-shadow-sm" :stroke-width="1.4" />
    <span
      v-if="initials"
      class="ink text-foreground/85 absolute right-2 bottom-1.5 font-display text-[10px] font-semibold tracking-wide"
    >
      {{ initials }}
    </span>
  </div>
</template>
