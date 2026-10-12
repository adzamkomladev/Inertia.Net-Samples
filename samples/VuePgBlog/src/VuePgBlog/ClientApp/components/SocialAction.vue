<script setup lang="ts">
import { computed, ref } from 'vue'
import { Link } from '@inertiajs/vue3'
import { cn } from '@/lib/utils'

const props = withDefaults(defineProps<{
  label: string
  count?: number
  active?: boolean
  tone: 'comment' | 'repost' | 'like' | 'bookmark'
  disabled?: boolean
  href?: string
}>(), { count: undefined, active: false, disabled: false })
const emit = defineEmits<{ click: [] }>()

const tones = {
  comment: { hover: 'hover:text-comment [&:hover>span:first-child]:bg-comment/10', active: 'text-comment' },
  repost: { hover: 'hover:text-repost [&:hover>span:first-child]:bg-repost/10', active: 'text-repost' },
  like: { hover: 'hover:text-like [&:hover>span:first-child]:bg-like/10', active: 'text-like' },
  bookmark: { hover: 'hover:text-bookmark [&:hover>span:first-child]:bg-bookmark/10', active: 'text-bookmark' },
}
// Only animate after a click, never when an active control first renders.
const bumped = ref(false)
const rootClass = computed(() => cn(
  '-ml-2 flex items-center rounded-full pr-1 text-muted-foreground transition-colors disabled:pointer-events-none disabled:opacity-50',
  tones[props.tone].hover, props.active && tones[props.tone].active,
))
const iconClass = computed(() => cn(
  'flex size-8 items-center justify-center rounded-full transition-colors [&_svg]:size-[18px]',
  props.active && '[&_svg]:fill-current', bumped.value && props.active && '[&_svg]:animate-pop',
))
const click = () => { bumped.value = true; emit('click') }
</script>

<template>
  <component
    :is="href ? Link : 'button'" :href="href" :type="href ? undefined : 'button'" :class="rootClass" :aria-label="label"
    :aria-pressed="href ? undefined : active" :disabled="href ? undefined : disabled" @click="href ? undefined : click()"
  >
    <span :class="iconClass" @animationend="bumped = false"><slot /></span>
    <span class="min-w-5 overflow-hidden text-left text-[13px] tabular-nums">
      <span v-if="count" :key="count" class="animate-tick inline-block">{{ count }}</span>
    </span>
    <span class="sr-only">{{ label }}</span>
  </component>
</template>
