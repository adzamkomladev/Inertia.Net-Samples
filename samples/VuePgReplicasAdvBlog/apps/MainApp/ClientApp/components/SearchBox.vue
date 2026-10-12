<script setup lang="ts">
import { router } from '@inertiajs/vue3'
import { useDebounceFn } from '@vueuse/core'
import { Search, X } from '@lucide/vue'
import { Input } from '@/components/ui/input'

const q = defineModel<string>({ default: '' })

const run = (value: string) =>
  router.get('/posts', value ? { q: value } : {}, { preserveState: true, replace: true, preserveScroll: true })
const debounced = useDebounceFn(run, 300)

function clear() {
  q.value = ''
  run('')
}
</script>

<template>
  <div class="relative w-full" role="search">
    <Search class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
    <Input
      v-model="q" type="search" placeholder="Search" aria-label="Search posts" autocomplete="off"
      class="bg-muted focus-visible:bg-background h-9 rounded-lg border-transparent pr-9 pl-9 shadow-none [&::-webkit-search-cancel-button]:hidden"
      @input="debounced(q.trim())" @keydown.esc="clear"
    />
    <button
      v-if="q" type="button" aria-label="Clear search"
      class="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-muted-foreground outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
      @click="clear"
    >
      <X class="size-4" />
    </button>
  </div>
</template>
