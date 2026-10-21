<script setup lang="ts">
import { computed } from 'vue'
import { Star } from '@lucide/vue'
import { cn } from '@/lib/utils'

const props = withDefaults(
  defineProps<{ rating: number; count?: number | null; interactive?: boolean; size?: 'sm' | 'md' }>(),
  { count: null, interactive: false, size: 'md' },
)

const emit = defineEmits<{ 'update:rating': [value: number] }>()

const full = computed(() => Math.round(props.rating))
const cls = computed(() => (props.size === 'sm' ? 'size-3.5' : 'size-4'))

const pick = (value: number) => {
  if (props.interactive) emit('update:rating', value)
}
</script>

<!-- Five-star readout; `interactive` turns it into a rating picker. -->
<template>
  <div class="flex items-center gap-1.5" :role="interactive ? 'radiogroup' : undefined" :aria-label="interactive ? 'Rating' : `Rated ${rating} out of 5`">
    <div :class="cn('flex items-center gap-0.5', interactive && 'gap-1')">
      <component
        :is="interactive ? 'button' : 'span'"
        v-for="i in 5"
        :key="i"
        :type="interactive ? 'button' : undefined"
        :aria-label="interactive ? `${i} star${i === 1 ? '' : 's'}` : undefined"
        :aria-checked="interactive ? full === i : undefined"
        :role="interactive ? 'radio' : undefined"
        class="p-0"
        @click="pick(i)"
      >
        <Star
          :class="cn(cls, 'transition-transform duration-200 ease-[var(--ease-spring)]', i <= full ? 'fill-warning text-warning' : 'text-muted-foreground/35', interactive && 'hover:scale-125')"
          aria-hidden="true"
        />
      </component>
    </div>
    <span v-if="count !== null" class="text-muted-foreground text-xs">
      <span class="price">{{ rating.toFixed(1) }}</span> ({{ count.toLocaleString() }})
    </span>
  </div>
</template>
