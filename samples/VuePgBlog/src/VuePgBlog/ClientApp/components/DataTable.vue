<script setup lang="ts">
import type { Component } from 'vue'
import EmptyState from '@/components/EmptyState.vue'

type Column = {
  key: string
  label: string
  align?: 'left' | 'right'
  class?: string
}

// Rows are plain records; cells come from the #cell-{key} slots when defined, else the raw value.
defineProps<{
  columns: Column[]
  rows: Record<string, unknown>[]
  rowKey: string
  emptyIcon?: Component
  emptyTitle?: string
  emptyDescription?: string
}>()

const cellSlots = (columns: Column[]) => columns.map((c) => `cell-${c.key}`)
</script>

<!-- Simple admin table with a stacked-card mobile fallback. -->
<template>
  <div class="bg-card overflow-hidden rounded-2xl border shadow-xs">
    <!-- Desktop table -->
    <div class="hidden overflow-x-auto md:block">
      <table class="w-full text-sm">
        <thead>
          <tr class="bg-muted/50 border-b text-left">
            <th
              v-for="col in columns" :key="col.key" scope="col"
              :class="[col.class, col.align === 'right' ? 'text-right' : 'text-left']"
              class="text-muted-foreground px-4 py-3 text-xs font-medium tracking-wide"
            >
              {{ col.label }}
            </th>
          </tr>
        </thead>
        <tbody class="divide-y">
          <tr v-for="row in rows" :key="String(row[rowKey])" class="transition-colors hover:bg-muted/30">
            <td
              v-for="col in columns" :key="col.key"
              :class="[col.class, col.align === 'right' ? 'text-right' : '']"
              class="px-4 py-3 align-middle"
            >
              <slot :name="`cell-${col.key}`" :row="row" :value="row[col.key]">{{ row[col.key] }}</slot>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Mobile cards -->
    <ul class="divide-y md:hidden">
      <li v-for="row in rows" :key="String(row[rowKey])" class="space-y-2 px-4 py-4">
        <div v-for="col in columns" :key="col.key" class="flex items-center justify-between gap-4">
          <span class="text-muted-foreground text-xs">{{ col.label }}</span>
          <span class="text-right" :class="col.class">
            <slot :name="`cell-${col.key}`" :row="row" :value="row[col.key]">{{ row[col.key] }}</slot>
          </span>
        </div>
      </li>
      <li v-if="!rows.length" class="px-4">
        <EmptyState :icon="emptyIcon ?? undefined" :title="emptyTitle ?? 'Nothing here yet'" :description="emptyDescription" />
      </li>
    </ul>

    <div v-if="!rows.length" class="hidden md:block border-t">
      <EmptyState :icon="emptyIcon ?? undefined" :title="emptyTitle ?? 'Nothing here yet'" :description="emptyDescription" />
    </div>
  </div>
</template>
