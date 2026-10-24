<script lang="ts">
  import { Link, useForm, usePage } from '@inertiajs/svelte'
  import Bell from '@lucide/svelte/icons/bell'
  import SidePanel from '@/components/SidePanel.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { fmtDateTime } from '@/lib/time'
  type Notice = { id: string; title: string; body?: string | null; href?: string | null; at: string; readAt?: string | null }
  const page = usePage()
  const inbox = $derived(page.props.notifications as { unreadCount: number; items: Notice[] } | null | undefined)
  let open = $state(false)
  const form = useForm({})
</script>
{#if page.props.auth?.user}
  <button type="button" aria-label={`Notifications${inbox?.unreadCount ? `, ${inbox.unreadCount} unread` : ''}`} onclick={() => open = true} class="hover:bg-accent relative grid size-11 shrink-0 place-items-center rounded-lg">
    <Bell class="size-5" aria-hidden="true" />
    {#if inbox?.unreadCount}<span class="bg-destructive absolute right-0 top-0 min-w-4 rounded-full px-1 text-center text-[10px] font-bold text-white">{inbox.unreadCount > 99 ? '99+' : inbox.unreadCount}</span>{/if}
  </button>
  <SidePanel bind:open title="Notifications" description="Your parking updates, reminders and account activity.">
    <div class="mb-5 flex flex-wrap items-center justify-between gap-2">
      <p class="text-muted-foreground text-sm" role="status">{inbox?.unreadCount ?? 0} unread</p>
      <Button variant="outline" size="sm" disabled={!inbox?.unreadCount || form.processing} onclick={() => form.post('/notifications/read-all', { preserveScroll: true })}>Mark all read</Button>
    </div>
    <ul class="divide-y">
      {#each inbox?.items ?? [] as item (item.id)}
        <li class="py-4">
          <div class="flex items-start gap-3">
            <span class={`mt-2 size-2 shrink-0 rounded-full ${item.readAt ? 'bg-muted' : 'bg-primary'}`} aria-label={item.readAt ? 'Read' : 'Unread'}></span>
            <div class="min-w-0 flex-1">
              {#if item.href}<Link href={item.href} onclick={() => open = false} class="font-semibold hover:underline">{item.title}</Link>{:else}<p class="font-semibold">{item.title}</p>{/if}
              {#if item.body}<p class="text-muted-foreground mt-1 break-words text-sm">{item.body}</p>{/if}
              <p class="text-muted-foreground mt-2 text-xs">{fmtDateTime(item.at)} UTC</p>
            </div>
          </div>
        </li>
      {:else}<li class="py-12 text-center"><Bell class="text-muted-foreground mx-auto mb-3 size-8" /><p class="font-medium">You're all caught up</p><p class="text-muted-foreground mt-2 text-sm">New updates will appear here, even when you're away.</p></li>{/each}
    </ul>
    <Button href="/account/alerts" variant="outline" class="mt-6 w-full" onclick={() => open = false}>View all notifications</Button>
  </SidePanel>
{/if}
