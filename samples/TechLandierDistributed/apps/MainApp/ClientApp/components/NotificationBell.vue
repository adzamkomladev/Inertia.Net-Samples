<script setup lang="ts">
import { computed, ref } from 'vue'
import { Link, useForm, usePage } from '@inertiajs/vue3'
import { Bell } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogTitle, DialogDescription } from '@/components/ui/dialog'
type Notice = { id: string; title: string; body: string; href: string | null; createdAt: string; readAt: string | null }
const page = usePage()
const inbox = computed(() => page.props.notifications as { unreadCount: number; items: Notice[] } | null | undefined)
const open = ref(false)
const form = useForm({})
</script>
<template>
  <template v-if="page.props.auth.user">
    <Button variant="ghost" size="icon" class="relative size-11 shrink-0" :aria-label="`Notifications${inbox?.unreadCount ? `, ${inbox.unreadCount} unread` : ''}`" @click="open = true">
      <Bell class="size-5" aria-hidden="true" /><span v-if="inbox?.unreadCount" class="bg-destructive absolute right-0 top-0 rounded-full px-1 text-[10px] font-bold text-white">{{ inbox.unreadCount > 99 ? '99+' : inbox.unreadCount }}</span>
    </Button>
    <Dialog v-model:open="open">
      <DialogContent class="inset-y-0 left-auto right-0 flex h-full w-full max-w-md translate-x-0 translate-y-0 flex-col gap-0 rounded-none p-0">
        <div class="border-b p-6 pr-12"><DialogTitle>Notifications</DialogTitle><DialogDescription class="mt-2">Orders, support replies and account updates, all in one place.</DialogDescription></div>
        <div class="min-h-0 flex-1 overflow-y-auto p-6">
          <div class="mb-5 flex flex-wrap items-center justify-between gap-2"><p class="text-muted-foreground text-sm" role="status">{{ inbox?.unreadCount ?? 0 }} unread</p><Button variant="outline" size="sm" :disabled="!inbox?.unreadCount || form.processing" @click="form.post('/account/alerts/read-all', { preserveScroll: true })">Mark all read</Button></div>
          <ul class="divide-y">
            <li v-for="item in inbox?.items ?? []" :key="item.id" class="flex items-start gap-3 py-4">
              <span :aria-label="item.readAt ? 'Read' : 'Unread'" :class="['mt-2 size-2 shrink-0 rounded-full', item.readAt ? 'bg-muted' : 'bg-primary']"></span>
              <div class="min-w-0 flex-1"><Link v-if="item.href" :href="item.href" class="font-semibold hover:underline" @click="open = false">{{ item.title }}</Link><p v-else class="font-semibold">{{ item.title }}</p><p class="text-muted-foreground mt-1 break-words text-sm">{{ item.body }}</p><p class="text-muted-foreground mt-2 text-xs">{{ new Date(item.createdAt).toLocaleString('en-US', { timeZone: 'UTC' }) }} UTC</p></div>
            </li>
            <li v-if="!inbox?.items.length" class="py-12 text-center"><Bell class="text-muted-foreground mx-auto mb-3 size-8" /><p class="font-medium">You're all caught up</p><p class="text-muted-foreground mt-2 text-sm">New updates will be kept here, even when you're away.</p></li>
          </ul>
          <Button variant="outline" class="mt-6 w-full" as-child><Link href="/account/alerts" @click="open = false">View all notifications</Link></Button>
        </div>
      </DialogContent>
    </Dialog>
  </template>
</template>
