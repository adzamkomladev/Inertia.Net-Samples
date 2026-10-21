<script setup lang="ts">
import { computed } from 'vue'
import { useMounted } from '@vueuse/core'

const props = defineProps<{ iso: string }>()

const absolute = computed(() =>
  new Intl.DateTimeFormat('en-US', { dateStyle: 'medium', timeZone: 'UTC' }).format(new Date(props.iso)))

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['month', 2_592_000], ['week', 604_800], ['day', 86_400], ['hour', 3_600], ['minute', 60],
]

// Absolute on the server and first client render (hydration-safe); relative after mount for < ~3 months.
const mounted = useMounted()

const text = computed(() => {
  if (!mounted.value) return absolute.value
  const diff = (new Date(props.iso).getTime() - Date.now()) / 1000
  if (Math.abs(diff) > 7_776_000) return absolute.value
  const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
  for (const [unit, secs] of UNITS) if (Math.abs(diff) >= secs) return rtf.format(Math.trunc(diff / secs), unit)
  return 'just now'
})
</script>

<template>
  <time :datetime="iso" :title="absolute">{{ text }}</time>
</template>
