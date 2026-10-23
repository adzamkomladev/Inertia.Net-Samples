<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'
import { fmtDate } from '@/lib/time'

const props = defineProps<{ startOn: string; endOn: string; active: boolean }>()

const DAY = 86_400_000
const day = (iso: string) => new Date(iso.length <= 10 ? `${iso}T00:00:00Z` : iso).getTime()
// Days are whole calendar days; SSR and the first client render agree within the same day.
const total = computed(() => Math.max(1, (day(props.endOn) - day(props.startOn)) / DAY))
const left = computed(() => Math.max(0, Math.ceil((day(props.endOn) - Date.now()) / DAY)))
const used = computed(() => Math.min(1, 1 - left.value / total.value))
</script>

<!-- Coverage window: how much of the cover is used and how many days remain. -->
<template>
  <div class="space-y-1.5">
    <div class="flex justify-between text-xs">
      <span class="text-muted-foreground">{{ fmtDate(startOn, true) }} → {{ fmtDate(endOn, true) }}</span>
      <span :class="cn('font-medium', !active ? 'text-muted-foreground' : left <= 30 ? 'text-warning' : 'text-foreground')">
        {{ active ? (left === 1 ? '1 day left' : `${left} days left`) : 'Ended' }}
      </span>
    </div>
    <div class="bg-muted h-1.5 overflow-hidden rounded-full" role="meter" :aria-valuenow="Math.round(used * 100)" aria-valuemin="0" aria-valuemax="100" aria-label="Coverage used">
      <div :class="cn('h-full rounded-full transition-[width] duration-700 ease-(--ease-out-soft)', !active ? 'bg-muted-foreground/40' : left <= 30 ? 'bg-warning' : 'bg-success')" :style="{ width: `${used * 100}%` }" />
    </div>
  </div>
</template>
