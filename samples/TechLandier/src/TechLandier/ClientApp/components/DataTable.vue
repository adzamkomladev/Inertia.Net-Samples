<script setup lang="ts">
import { computed, type Component } from 'vue'
import { Link } from '@inertiajs/vue3'
import EmptyState from '@/components/EmptyState.vue'

type Column = {
  key: string
  /** Column caption: both spellings accepted (older pages use header, newer use label). */
  header?: string
  label?: string
  align?: 'left' | 'right'
  class?: string
  /** Card title in the stacked mobile view (defaults to the first column). */
  primary?: boolean
}

// Rows are plain records; cells come from the #cell-{key} slots when defined, else the raw value.
const props = defineProps<{
  columns: Column[]
  rows: readonly any[]
  rowHref?: (row: any) => string
  rowKey: string
  emptyIcon?: Component
  emptyTitle?: string
  emptyDescription?: string
}>()

const caption = (c: Column) => c.label ?? c.header ?? ''
const primary = computed(() => props.columns.find((c) => c.primary) ?? props.columns[0]!)
const rest = computed(() => props.columns.filter((c) => c !== primary.value))
</script>

<!-- Table on md+, one stacked card per row below it. Same slots drive both. -->
<template>
  <div class="surface overflow-hidden">
    <div class="hidden overflow-x-auto md:block">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-muted-foreground border-b text-left text-xs">
            <th
              v-for="col in columns" :key="col.key" scope="col"
              :class="[col.class, col.align === 'right' ? 'text-right' : 'text-left']"
              class="px-4 py-3 font-medium"
            >
              {{ caption(col) }}
            </th>
          </tr>
        </thead>
        <tbody class="divide-y">
          <tr v-for="row in rows" :key="String(row[rowKey])" class="hover:bg-muted/40 transition-colors">
            <td
              v-for="col in columns" :key="col.key"
              :class="[col.class, col.align === 'right' ? 'text-right' : '']"
              class="px-4 py-3 align-middle"
            >
              <Link v-if="rowHref && col === primary" :href="rowHref(row)" class="block rounded-sm text-primary underline-offset-4 hover:underline focus-visible:outline-2"><slot :name="`cell-${col.key}`" :row="row" :value="row[col.key]">{{ row[col.key] }}</slot></Link>
              <slot v-else :name="`cell-${col.key}`" :row="row" :value="row[col.key]">{{ row[col.key] }}</slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <ul class="divide-y md:hidden">
      <li v-for="row in rows" :key="String(row[rowKey])" class="space-y-3 p-4">
        <div class="min-w-0 font-medium">
          <Link v-if="rowHref" :href="rowHref(row)" class="block text-primary hover:underline"><slot :name="`cell-${primary.key}`" :row="row" :value="row[primary.key]">{{ row[primary.key] }}</slot></Link>
          <slot v-else :name="`cell-${primary.key}`" :row="row" :value="row[primary.key]">{{ row[primary.key] }}</slot>
        </div>
        <dl class="grid grid-cols-2 gap-x-4 gap-y-2.5">
          <div v-for="col in rest" :key="col.key" class="min-w-0 text-sm">
            <dt class="text-muted-foreground text-xs">{{ caption(col) }}</dt>
            <dd class="mt-0.5 min-w-0" :class="col.class">
              <slot :name="`cell-${col.key}`" :row="row" :value="row[col.key]">{{ row[col.key] }}</slot>
            </dd>
          </div>
        </dl>
      </li>
    </ul>

    <EmptyState v-if="!rows.length" :icon="emptyIcon ?? undefined" :title="emptyTitle ?? 'Nothing here yet'" :description="emptyDescription" />
  </div>
</template>
