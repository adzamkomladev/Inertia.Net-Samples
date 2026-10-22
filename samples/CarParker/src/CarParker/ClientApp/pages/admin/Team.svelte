<script lang="ts">
  import { useForm, usePage } from '@inertiajs/svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  let { members }: { members: { id: string; name: string; email: string; role: string }[] } = $props()
  const page = usePage()
  const form = useForm({ name: '', email: '', password: '', role: 'guard' })
  const removeForm = useForm({})
  function remove(id: string) { if (!removeForm.processing) removeForm.post(`/admin/team/${id}/remove`, { preserveScroll: true }) }
</script>
<PageTitle title="Team management" />

<div class="space-y-6">
<PageHeader title="Team management" description="Add administrators and guards. Admins manage the business; guards operate the parking console." />
<div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
<section class="bg-card rounded-xl border p-6">
<h2 class="mb-4 font-semibold">Staff ({members.length})</h2>
<ul class="divide-y">
{#each members as member (member.id)}<li class="flex flex-wrap items-center gap-3 py-4">
<div class="min-w-0 flex-1">
<p class="font-medium">{member.name} <span class="text-xs text-muted-foreground">{member.role}</span>
</p>
<p class="break-all text-sm text-muted-foreground">{member.email}</p>
</div>
{#if member.id !== page.props.auth.user?.id}<ConfirmDialog title="Remove staff access?" description="Their customer account and bookings will be kept." confirmLabel="Remove access" destructive onConfirm={() => remove(member.id)} triggerClass="rounded-md border px-3 py-2 text-sm hover:bg-muted">
{#snippet trigger()}Remove access{/snippet}</ConfirmDialog>{/if}</li>{/each}</ul>
</section>
<form class="bg-card space-y-4 rounded-xl border p-6" onsubmit={(event) => { event.preventDefault(); form.post('/admin/team', { preserveScroll: true, onSuccess: () => form.reset() }) }}>
<h2 class="font-semibold">Add team member</h2>
<div class="space-y-2">
<Label for="staff-name">Name</Label>
<Input id="staff-name" bind:value={form.name} required minlength={2} maxlength={80} />
</div>
<div class="space-y-2">
<Label for="staff-email">Email</Label>
<Input id="staff-email" bind:value={form.email} type="email" required maxlength={254} />
</div>
<div class="space-y-2">
<Label for="staff-password">Initial password</Label>
<Input id="staff-password" bind:value={form.password} type="password" autocomplete="new-password" required minlength={12} maxlength={128} />
</div>
<div class="space-y-2">
<Label for="staff-role">Role</Label>
<Select id="staff-role" bind:value={form.role} items={[{ value: 'guard', label: 'Guard' }, { value: 'admin', label: 'Administrator' }]} />
</div>
{#each Object.values(form.errors) as error}<p class="text-sm text-destructive" role="alert">{error}</p>{/each}<Button type="submit" disabled={form.processing}>Create team member</Button>
</form>
</div>
</div>
