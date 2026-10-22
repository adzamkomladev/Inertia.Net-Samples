<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import { BadgeCheck, ShieldCheck, Users2 } from '@lucide/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import { fmtDate } from '@/lib/time'
  import type { AdminUser } from '@/types'

  /** GET /admin/users — recent sign-ups with verification toggles. */
  let { users }: { users: AdminUser[] } = $props()

  const verifyForm = useForm({})
  const toggle = (id: string) => verifyForm.post(`/admin/users/${id}/verify`)

  const cols = [
    { key: 'name', header: 'Name', primary: true, cell: (u: AdminUser) => u.name },
    { key: 'email', header: 'Email', cell: (u: AdminUser) => u.email },
    { key: 'role', header: 'Role', cell: (u: AdminUser) => u.role },
    { key: 'verified', header: 'Verified', cell: (u: AdminUser) => (u.emailVerifiedAt ? 'yes' : 'no') },
    { key: 'joined', header: 'Joined', cell: (u: AdminUser) => fmtDate(u.createdAt, false) },
  ]
</script>

<PageTitle title="Users" />

<div class="flex flex-col gap-6">
  <PageHeader title="Users" description="The newest 200 accounts — customers, guards and admins." />

  {#if users.length}
    <div class="bg-card shadow-card overflow-hidden rounded-xl">
      <DataTable columns={cols} rows={users} rowKey={(u) => u.id} emptyTitle="No users" />
      <!-- mobile toggle actions live under the table cards; desktop uses the row list below -->
    </div>
    <div class="flex flex-col gap-3">
      {#each users.slice(0, 24) as user (user.id)}
        <div class="bg-card shadow-card flex items-center gap-3 rounded-xl p-4">
          <span class="bg-primary/10 text-primary grid size-9 place-items-center rounded-xl">
            {#if user.role === 'admin'}<ShieldCheck class="size-4" aria-hidden="true" />{:else if user.role === 'guard'}<BadgeCheck class="size-4" aria-hidden="true" />{:else}<Users2 class="size-4" aria-hidden="true" />{/if}
          </span>
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium">{user.name}</p>
            <p class="text-muted-foreground truncate text-xs">{user.email}</p>
          </div>
          <StatusBadge status={user.role === 'admin' ? 'resolved' : user.role === 'guard' ? 'booked' : 'pending'} label={user.role} size="xs" />
          <StatusBadge status={user.emailVerifiedAt ? 'paid' : 'requested'} label={user.emailVerifiedAt ? 'verified' : 'unverified'} size="xs" />
          <button
            class="border-input hover:bg-muted rounded-full border px-3 py-1.5 text-xs font-medium disabled:opacity-50"
            onclick={() => toggle(user.id)}
            disabled={verifyForm.processing}
          >
            {user.emailVerifiedAt ? 'Revoke' : 'Mark verified'}
          </button>
        </div>
      {/each}
    </div>
  {:else}
    <EmptyState icon={Users2} title="No users" description="New registrations appear here." />
  {/if}
</div>
