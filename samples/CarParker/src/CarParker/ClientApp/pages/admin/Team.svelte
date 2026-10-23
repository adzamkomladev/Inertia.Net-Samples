<script lang="ts">
  import { useForm, usePage } from '@inertiajs/svelte'
  import { RadioGroup } from 'bits-ui'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import UserPlus from '@lucide/svelte/icons/user-plus'
  import UserRoundX from '@lucide/svelte/icons/user-round-x'
  import UsersRound from '@lucide/svelte/icons/users-round'
  import Siren from '@lucide/svelte/icons/siren'
  import ConfirmDialog from '@/components/ConfirmDialog.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import SidePanel from '@/components/SidePanel.svelte'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import { cn, initials } from '@/lib/utils'

  /** GET/POST /admin/team: staff directory, add-member sheet and access removal. */
  let { members }: { members: { id: string; name: string; email: string; role: string }[] } = $props()

  let open = $state(false)
  let q = $state('')
  const filtered = $derived(members.filter(m => `${m.name} ${m.email} ${m.role}`.toLowerCase().includes(q.trim().toLowerCase())))
  const page = usePage()
  const form = useForm({ name: '', email: '', password: '', role: 'guard' })
  const removeForm = useForm({})
  let removing = $state<string | null>(null)
  const remove = (id: string) => {
    if (removeForm.processing) return
    removing = id
    removeForm.post(`/admin/team/${id}/remove`, { preserveScroll: true, onFinish: () => (removing = null) })
  }
  const create = () => form.post('/admin/team', { preserveScroll: true, onSuccess: () => { form.reset(); open = false } })

  const roles = [
    { value: 'guard', label: 'Guard', description: 'Runs the gate: check-ins, check-outs and incidents.', icon: Siren },
    { value: 'admin', label: 'Administrator', description: 'Manages lots, money, claims and the team.', icon: ShieldCheck },
  ]
  const count = (role: string) => members.filter(m => m.role === role).length
</script>

<PageTitle title="Team" />

{#snippet error(message: string | undefined)}
  {#if message}<p class="text-destructive animate-rise text-sm" role="alert">{message}</p>{/if}
{/snippet}

<div class="flex flex-col gap-6">
  <PageHeader title="Team" description="Give the right people access to your parking operations.">
    {#snippet actions()}<Button onclick={() => (open = true)}><UserPlus class="size-4" aria-hidden="true" /> Add member</Button>{/snippet}
  </PageHeader>

  <div class="flex flex-col gap-3 sm:flex-row sm:items-center">
    <SearchField bind:value={q} label="Search team" placeholder="Search staff by name, email or role…" />
    <p class="text-muted-foreground shrink-0 text-sm" role="status">{count('admin')} administrators · {count('guard')} guards</p>
  </div>

  {#if filtered.length}
    <ul class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {#each filtered as member (member.id)}
        {@const self = member.id === page.props.auth.user?.id}
        <li class={cn('bg-card shadow-card animate-rise flex flex-col gap-4 rounded-xl p-5 ring-1 ring-foreground/5 transition-opacity', removing === member.id && 'opacity-50')}>
          <div class="flex items-start gap-3">
            <Avatar fallback={initials(member.name)} class="size-11" fallbackClass={cn('text-sm font-bold', member.role === 'admin' ? 'bg-primary text-primary-foreground' : 'bg-spot-booked/15 text-spot-booked')} />
            <div class="min-w-0 flex-1">
              <a href={`/admin/users/${member.id}`} class="block truncate font-semibold hover:underline">{member.name}</a>
              <p class="text-muted-foreground truncate text-sm">{member.email}</p>
            </div>
            {#if self}<span class="bg-muted rounded-full px-2 py-0.5 text-xs font-semibold">You</span>{/if}
          </div>
          <div class="mt-auto flex items-center justify-between gap-2 border-t pt-3">
            <StatusBadge status={member.role === 'admin' ? 'scheduled' : 'confirmed'} label={member.role === 'admin' ? 'Administrator' : 'Guard'} size="xs" />
            {#if !self}
              <ConfirmDialog
                title={`Remove ${member.name}'s staff access?`}
                description="They keep their customer account and bookings, and lose console access immediately."
                confirmLabel="Remove access"
                destructive
                onConfirm={() => remove(member.id)}
                triggerClass="text-muted-foreground hover:text-destructive hover:bg-destructive/10 inline-flex h-8 items-center gap-1.5 rounded-md px-2.5 text-xs font-semibold transition-colors"
              >
                {#snippet trigger()}
                  {#if removing === member.id}<Loader2 class="size-3.5 animate-spin" aria-hidden="true" />{:else}<UserRoundX class="size-3.5" aria-hidden="true" />{/if} Remove access
                {/snippet}
              </ConfirmDialog>
            {/if}
          </div>
        </li>
      {/each}
    </ul>
  {:else}
    <div class="bg-card shadow-card rounded-xl"><EmptyState icon={UsersRound} title="No staff match your search" description="Try a name, email or role such as guard." /></div>
  {/if}
</div>

<SidePanel bind:open title="Add team member" description="They sign in with this email and the initial password; ask them to change it after their first shift.">
  <form id="add-member" novalidate class="grid gap-5" onsubmit={(event) => { event.preventDefault(); create() }}>
    <fieldset class="grid gap-2">
      <legend class="mb-2 text-sm font-medium">Role</legend>
      <RadioGroup.Root bind:value={form.role} aria-label="Role" class="grid gap-2 sm:grid-cols-2">
        {#each roles as role (role.value)}
          <RadioGroup.Item
            value={role.value}
            class="ring-input hover:bg-muted data-[state=checked]:bg-primary/8 data-[state=checked]:ring-primary focus-visible:ring-ring group flex flex-col items-start gap-1.5 rounded-xl p-4 text-left ring-1 ring-inset transition-[background-color,box-shadow,transform] outline-none focus-visible:ring-2 active:scale-[0.98] data-[state=checked]:ring-2 data-[state=checked]:hover:bg-primary/8"
          >
            <role.icon class="text-muted-foreground group-data-[state=checked]:text-primary size-5" aria-hidden="true" />
            <span class="font-semibold">{role.label}</span>
            <span class="text-muted-foreground text-xs leading-5">{role.description}</span>
          </RadioGroup.Item>
        {/each}
      </RadioGroup.Root>
      {@render error(form.errors.role)}
    </fieldset>
    <div class="grid gap-2">
      <Label for="staff-name">Name</Label>
      <Input id="staff-name" bind:value={form.name} required minlength={2} maxlength={80} autocomplete="name" aria-invalid={!!form.errors.name || undefined} />
      {@render error(form.errors.name)}
    </div>
    <div class="grid gap-2">
      <Label for="staff-email">Email</Label>
      <Input id="staff-email" bind:value={form.email} type="email" required maxlength={254} autocomplete="email" aria-invalid={!!form.errors.email || undefined} />
      {@render error(form.errors.email)}
    </div>
    <div class="grid gap-2">
      <Label for="staff-password">Initial password</Label>
      <Input id="staff-password" bind:value={form.password} type="password" autocomplete="new-password" required minlength={12} maxlength={128} aria-invalid={!!form.errors.password || undefined} />
      <p class="text-muted-foreground text-xs">At least 12 characters.</p>
      {@render error(form.errors.password)}
    </div>
  </form>
  {#snippet footer()}
    <Button variant="ghost" onclick={() => (open = false)}>Cancel</Button>
    <Button type="submit" form="add-member" disabled={form.processing}>
      {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<UserPlus class="size-4" aria-hidden="true" />{/if}
      {form.processing ? 'Adding…' : 'Add member'}
    </Button>
  {/snippet}
</SidePanel>
