<script lang="ts">
  import SidePanel from '@/components/SidePanel.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import { useForm, usePage } from '@inertiajs/svelte'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  let { members }: { members: { id: string; name: string; email: string; role: string }[] } = $props()
  let open = $state(false)
  let q = $state('')
  const filtered = $derived(members.filter(m => `${m.name} ${m.email} ${m.role}`.toLowerCase().includes(q.trim().toLowerCase())))
  const page = usePage()
  const form = useForm({ name: '', email: '', password: '', role: 'guard' })
  const removeForm = useForm({})
  function remove(id: string) { if (!removeForm.processing) removeForm.post(`/admin/team/${id}/remove`, { preserveScroll: true }) }
</script>
<PageTitle title="Team management" />

<div class="space-y-6">
<PageHeader title="Team management" description="Give the right people access to your parking operations.">
{#snippet actions()}<Button onclick={() => open = true}>Add team member</Button>{/snippet}</PageHeader>
<SearchField bind:value={q} label="Search team" placeholder="Search staff by name, email or role…" />
<div class="grid gap-6 ">
<section class="bg-card rounded-xl border p-6">
<h2 class="mb-4 font-semibold">Staff ({members.length})</h2>
<ul class="divide-y">
{#each filtered as member (member.id)}<li class="flex flex-wrap items-center gap-3 py-4">
<div class="min-w-0 flex-1">
<p class="font-medium"><a href={`/admin/users/${member.id}`} class="hover:underline">{member.name}</a> <span class="text-xs text-muted-foreground">{member.role}</span>
</p>
<p class="break-all text-sm text-muted-foreground">{member.email}</p>
</div>
{#if member.id !== page.props.auth.user?.id}<ConfirmDialog title="Remove staff access?" description="Their customer account and bookings will be kept." confirmLabel="Remove access" destructive onConfirm={() => remove(member.id)} triggerClass="rounded-md border px-3 py-2 text-sm hover:bg-muted">
{#snippet trigger()}Remove access{/snippet}</ConfirmDialog>{/if}</li>{:else}<li class="py-8 text-center text-sm text-muted-foreground">No staff match your search.</li>{/each}</ul>
</section>
<SidePanel bind:open title="Add team member" description="Administrators manage the business. Guards operate the gate.">
<form class="bg-card space-y-4 rounded-xl border p-6" onsubmit={(event) => { event.preventDefault(); form.post('/admin/team', { preserveScroll: true, onSuccess: () => { form.reset(); open = false } }) }}>
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
</SidePanel>
</div>
</div>
