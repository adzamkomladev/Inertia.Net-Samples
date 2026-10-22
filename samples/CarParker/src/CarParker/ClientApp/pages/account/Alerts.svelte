<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import BellOff from '@lucide/svelte/icons/bell-off'
  import Inbox from '@lucide/svelte/icons/inbox'
  import AlertList from '@/components/AlertList.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import { buttonVariants } from '@/components/ui/button/variants'
  import { cn } from '@/lib/utils'
  import type { AlertItem } from '@/types'

  /** GET /account/alerts — alert feed; POST /account/alerts/read marks ids read. */
  let { alerts }: { alerts: AlertItem[] } = $props()

  const unreadIds = $derived(alerts.filter(a => !a.readAt).map(a => a.id))

  const readForm = useForm({ ids: [] as string[] })
  const markRead = () => readForm.transform(data => ({ ...data, ids: unreadIds })).post('/account/alerts/read')
</script>

<PageTitle title="Alerts" />
<div class="flex flex-col gap-6">
  <PageHeader title="Alerts" description="Window reminders, overstays, refunds and cover decisions.">
    {#snippet actions()}
      {#if unreadIds.length}
        <ConfirmDialog
          title="Mark all as read?"
          description={`${unreadIds.length} unread alert${unreadIds.length === 1 ? '' : 's'} will be marked read.`}
          confirmLabel="Mark read"
          onConfirm={markRead}
          triggerClass={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'rounded-2xl')}
        >
          {#snippet trigger()}Mark all read{/snippet}
        </ConfirmDialog>
      {/if}
    {/snippet}
  </PageHeader>

  {#if alerts.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <AlertList {alerts} />
    </div>
  {:else}
    <div class="bg-card shadow-card rounded-xl">
      <EmptyState icon={Inbox} title="No alerts yet" description="Booking reminders, overstay warnings and refund decisions will land here." class="py-14">
        <Button href="/lots" variant="outline" ><BellOff class="size-4" aria-hidden="true" /> Nothing to do</Button>
      </EmptyState>
    </div>
  {/if}
</div>
