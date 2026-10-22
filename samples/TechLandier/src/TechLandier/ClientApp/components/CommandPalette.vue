<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { router } from '@inertiajs/vue3'
import { Search } from '@lucide/vue'
import { useEventListener } from '@vueuse/core'
import { Dialog, DialogContent, DialogDescription, DialogTitle } from '@/components/ui/dialog'
import { cn } from '@/lib/utils'
import type { NavItem } from '@/components/SidebarShell.vue'

const props = defineProps<{ items: NavItem[] }>()
const open = defineModel<boolean>('open', { default: false })

const query = ref('')
const active = ref(0)
const input = ref<HTMLInputElement | null>(null)

const results = computed(() => {
  const q = query.value.trim().toLowerCase()
  return q ? props.items.filter((i) => i.label.toLowerCase().includes(q)) : props.items
})

watch(open, async (o) => {
  if (!o) return
  query.value = ''
  active.value = 0
  await nextTick()
  input.value?.focus()
})
watch(query, () => (active.value = 0))

useEventListener(document, 'keydown', (e: KeyboardEvent) => {
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    open.value = !open.value
  }
})

const go = (item?: NavItem) => {
  if (!item) return
  open.value = false
  router.visit(item.href)
}
const onKey = (e: KeyboardEvent) => {
  if (e.key === 'ArrowDown') (e.preventDefault(), (active.value = Math.min(results.value.length - 1, active.value + 1)))
  else if (e.key === 'ArrowUp') (e.preventDefault(), (active.value = Math.max(0, active.value - 1)))
  else if (e.key === 'Enter') (e.preventDefault(), go(results.value[active.value]))
}
</script>

<!-- Jump-to palette: type to filter pages, arrows + Enter to go. -->
<template>
  <Dialog v-model:open="open">
    <DialogContent :show-close-button="false" class="top-[20%] translate-y-0 gap-0 p-0 sm:max-w-md">
      <DialogTitle class="sr-only">Jump to a page</DialogTitle>
      <DialogDescription class="sr-only">Type to filter, use the arrow keys and Enter to open a page.</DialogDescription>
      <div class="flex items-center gap-3 border-b px-4">
        <Search class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
        <input
          ref="input" v-model="query" type="text" placeholder="Jump to…" aria-label="Jump to a page" role="combobox" aria-expanded="true" aria-controls="palette-list"
          :aria-activedescendant="results[active] ? `palette-${active}` : undefined"
          class="placeholder:text-muted-foreground h-12 w-full bg-transparent text-base outline-none md:text-sm" @keydown="onKey"
        />
      </div>
      <ul id="palette-list" role="listbox" class="max-h-80 overflow-y-auto p-2">
        <li
          v-for="(item, i) in results" :id="`palette-${i}`" :key="item.href" role="option" :aria-selected="i === active"
          :class="cn('flex h-11 cursor-pointer items-center gap-3 rounded-lg px-3 text-sm', i === active && 'bg-muted')" @mousemove="active = i" @click="go(item)"
        >
          <component :is="item.icon" class="text-muted-foreground size-4" aria-hidden="true" /> {{ item.label }}
        </li>
        <li v-if="!results.length" class="text-muted-foreground px-3 py-6 text-center text-sm">No page matches “{{ query }}”.</li>
      </ul>
    </DialogContent>
  </Dialog>
</template>
