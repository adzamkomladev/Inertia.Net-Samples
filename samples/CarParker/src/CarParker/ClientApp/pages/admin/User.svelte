<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import DataTable from '@/components/DataTable.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import { fmtDate, fmtWindow } from '@/lib/time'
  import { money } from '@/lib/money'
  import type { AdminUser, BookingView } from '@/types'
  let { detail }: { detail: { user: AdminUser; bookings: BookingView[] } } = $props()
  const form = useForm({})
</script>
<PageTitle title={detail.user.name} />
<div class="space-y-6">
  <a href="/admin/users" class="text-primary text-sm hover:underline">← All people</a>
  <PageHeader title={detail.user.name} description="Account details and recent parking activity." />
  <div class="grid items-start gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
    <section class="bg-card overflow-hidden rounded-xl border">
      <h2 class="border-b p-5 text-lg font-semibold">Recent bookings</h2>
      <DataTable rows={detail.bookings} rowKey={b => b.id} rowHref={b => `/admin/bookings/${b.id}`} emptyTitle="No bookings yet" columns={[
        { key: 'lot', header: 'Lot / bay', primary: true, cell: (b: BookingView) => `${b.lotName} · ${b.spotLabel}` },
        { key: 'start', header: 'Start', cell: (b: BookingView) => b.windowType === 'recurring' ? `${b.occurrenceCount} weekly visits` : fmtWindow(b.startAtUtc, b.endAtUtc) },
        { key: 'status', header: 'Status', cell: (b: BookingView) => b.status },
        { key: 'total', header: 'Total', cell: (b: BookingView) => money(b.totalCents) },
      ]} />
      <p class="text-muted-foreground border-t p-4 text-xs">Up to 100 most recent bookings.</p>
    </section>
    <aside class="bg-card space-y-5 rounded-xl border p-5 xl:sticky xl:top-6">
      <h2 class="font-semibold">Account information</h2>
      <div class="flex flex-wrap gap-2"><StatusBadge status="booked" label={detail.user.role} /><StatusBadge status={detail.user.emailVerifiedAt ? 'paid' : 'requested'} label={detail.user.emailVerifiedAt ? 'Verified' : 'Unverified'} /></div>
      <dl class="space-y-4 text-sm"><div><dt class="text-muted-foreground">Email</dt><dd class="mt-1 break-all">{detail.user.email}</dd></div><div><dt class="text-muted-foreground">Joined</dt><dd class="mt-1">{fmtDate(detail.user.createdAt, true)}</dd></div><div><dt class="text-muted-foreground">Account ID</dt><dd class="mt-1 break-all text-xs">{detail.user.id}</dd></div></dl>
      <Button variant="outline" disabled={form.processing} onclick={() => form.post(`/admin/users/${detail.user.id}/verify`, { preserveScroll: true })}>{detail.user.emailVerifiedAt ? 'Revoke verification' : 'Mark email verified'}</Button>
      <p class="text-muted-foreground text-xs">Only verify an email after confirming ownership.</p>
    </aside>
  </div>
</div>
