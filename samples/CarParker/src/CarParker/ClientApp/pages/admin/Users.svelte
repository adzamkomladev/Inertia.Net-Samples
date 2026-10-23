<script lang="ts">
  import { untrack } from 'svelte'
  import { useForm, router } from '@inertiajs/svelte'
  import DataTable from '@/components/DataTable.svelte'
  import Pagination from '@/components/Pagination.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import SidePanel from '@/components/SidePanel.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import { fmtDate } from '@/lib/time'
  import type { AdminUser } from '@/types'
  let { users, query = '', roleFilter = '', page = 1, hasMore = false }: { users: AdminUser[]; query?: string; roleFilter?: string; page: number; hasMore: boolean } = $props()
  let open = $state(false)
  let q = $state(untrack(() => query))
  $effect.pre(() => { q = query })
  useDebouncedSearch(() => q, '/admin/users', () => ({ role: roleFilter }))
  const form = useForm({ name: '', email: '', password: '' })
  const roles = [{ value: '', label: 'All roles' }, { value: 'customer', label: 'Customers' }, { value: 'guard', label: 'Guards' }, { value: 'admin', label: 'Administrators' }]
</script>
<PageTitle title="People" />
<div class="space-y-6">
  <PageHeader title="People" description="Customer accounts and staff, all in one place.">
    {#snippet actions()}<Button onclick={() => open = true}>Create customer</Button>{/snippet}
  </PageHeader>
  <div class="bg-card rounded-xl border p-4 space-y-3">
    <div class="flex flex-col gap-3 sm:flex-row">
      <SearchField bind:value={q} label="Search users" placeholder="Search by name or email…" />
      <Select items={roles} value={roleFilter} onValueChange={(role) => router.get('/admin/users', { q, role }, { preserveState: true, preserveScroll: true })} ariaLabel="Filter users by role" class="sm:w-48" />
    </div>
    <p class="text-muted-foreground text-sm" role="status">{users.length} accounts on this page</p>
  </div>
  <div class="bg-card overflow-hidden rounded-xl border">
    <DataTable rows={users} rowKey={u => u.id} rowHref={u => `/admin/users/${u.id}`} emptyTitle="No matching accounts" emptyDescription="Try a different name, email or role." columns={[
      { key: 'name', header: 'Name', primary: true, cell: (u: AdminUser) => u.name },
      { key: 'email', header: 'Email', cell: (u: AdminUser) => u.email },
      { key: 'role', header: 'Role', cell: (u: AdminUser) => u.role },
      { key: 'verified', header: 'Verification', cell: (u: AdminUser) => u.emailVerifiedAt ? 'Verified' : 'Awaiting verification' },
      { key: 'joined', header: 'Joined', cell: (u: AdminUser) => fmtDate(u.createdAt, false) },
    ]} />
  </div>
  {#if page > 1 || hasMore}<Pagination href="/admin/users" {page} {hasMore} params={{ q: query, role: roleFilter }} />{/if}
  <p class="text-muted-foreground text-sm">Use Details to view an account and its bookings. <a href="/admin/team" class="text-primary underline">Manage staff access →</a></p>
</div>
<SidePanel bind:open title="Create customer" description="Create a customer account. Share the initial password securely; the customer verifies their own email.">
  <form class="space-y-5" onsubmit={e => { e.preventDefault(); form.post('/admin/users', { preserveScroll: true, onSuccess: () => { open = false; form.reset() } }) }}>
    <div class="space-y-2"><Label for="customer-name">Full name</Label><Input id="customer-name" bind:value={form.name} autocomplete="name" required minlength={2} maxlength={80} /></div>
    <div class="space-y-2"><Label for="customer-email">Email address</Label><Input id="customer-email" type="email" bind:value={form.email} autocomplete="email" required maxlength={254} /></div>
    <div class="space-y-2"><Label for="customer-password">Initial password</Label><Input id="customer-password" type="password" bind:value={form.password} autocomplete="new-password" required minlength={12} maxlength={128} /><p class="text-muted-foreground text-sm">Use at least 12 characters.</p></div>
    {#each Object.values(form.errors) as error}<p class="text-destructive text-sm" role="alert">{error}</p>{/each}
    <div class="flex gap-3 border-t pt-5"><Button type="submit" disabled={form.processing}>{form.processing ? 'Creating…' : 'Create customer'}</Button><Button variant="outline" onclick={() => open = false}>Cancel</Button></div>
  </form>
</SidePanel>
