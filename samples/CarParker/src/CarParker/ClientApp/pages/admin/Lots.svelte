<script lang="ts">
  import SidePanel from '@/components/SidePanel.svelte'
  import SearchField from '@/components/SearchField.svelte'
  import type { LotCard as LotCardType } from '@/types'
  import Select from '@/components/ui/select/Select.svelte'
  import { useForm } from '@inertiajs/svelte'
  import Building2 from '@lucide/svelte/icons/building-complex'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import Plus from '@lucide/svelte/icons/plus'
  import ShieldOff from '@lucide/svelte/icons/shield-off'
  import StatusBadge from '@/components/StatusBadge.svelte'
  import LotCard from '@/components/LotCard.svelte'
  import LotForm from '@/components/LotForm.svelte'
  import { lotFields } from '@/lib/lot-form'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'

  /** GET/POST /admin/lots — directory + create form (spawns the standard A–D grid). */
  let { lots }: { lots: LotCardType[] } = $props()

  let open = $state(false)
  let q = $state('')
  let status = $state('all')
  const filtered = $derived(lots.filter(lot => `${lot.name} ${lot.area} ${lot.address}`.toLowerCase().includes(q.trim().toLowerCase()) && (status === 'all' || lot.isActive === (status === 'open'))))

  const form = useForm(lotFields())

  const create = () => form.post('/admin/lots', { onSuccess: () => { form.reset(); open = false } })
</script>

<PageTitle title="Lots & spots" />

<div class="flex flex-col gap-6">
  <PageHeader title="Lots & spots" description="Manage availability, pricing and the places people park.">
    {#snippet actions()}<Button onclick={() => open = true}><Plus class="size-4" />Create lot</Button>{/snippet}
  </PageHeader>
  <div class="bg-card flex flex-col gap-3 rounded-xl border p-4 sm:flex-row">
    <SearchField bind:value={q} label="Search lots" placeholder="Search lots, areas or addresses…" />
    <Select bind:value={status} items={[{ value: 'all', label: 'All lots' }, { value: 'open', label: 'Open for bookings' }, { value: 'closed', label: 'Closed' }]} ariaLabel="Filter lots" class="sm:w-48" />
  </div>
  <p class="text-muted-foreground text-sm" role="status">{filtered.length} of {lots.length} lots</p>

  <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
    {#each filtered as lot (lot.id)}
      <div class="relative">
        <LotCard {lot} href={`/admin/lots/${lot.id}`} />
        {#if !lot.isActive || !lot.insuranceCovered}
          <span class="pointer-events-none absolute right-4 bottom-4 flex items-center gap-1.5">
            {#if !lot.isActive}<StatusBadge status="blocked" label="Closed" size="xs" />{/if}
            {#if !lot.insuranceCovered}<span class="bg-muted rounded-full p-1" title="Spot Cover disabled"><ShieldOff class="text-muted-foreground size-3.5" aria-hidden="true" /></span>{/if}
          </span>
        {/if}
      </div>
    {:else}
      <div class="rounded-2xl border border-dashed p-12 text-center sm:col-span-2 xl:col-span-3">
        <Building2 class="text-muted-foreground mx-auto size-8" strokeWidth={1.5} aria-hidden="true" />
        <p class="font-display mt-3 font-semibold">No matching lots</p>
        <p class="text-muted-foreground mx-auto mt-1 max-w-sm text-sm">Try another search or create a new lot.</p>
      </div>
    {/each}
  </div>

  <!-- Create lot -->
  <SidePanel bind:open title="Create lot" description="Set up a location, its pricing and opening hours. A fresh bay grid is created automatically.">
    <LotForm {form} creating id="create-lot" onsubmit={create} />
    {#snippet footer()}
      <Button variant="ghost" onclick={() => (open = false)}>Cancel</Button>
      <Button type="submit" form="create-lot" disabled={form.processing}>
        {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Plus class="size-4" aria-hidden="true" />{/if}
        {form.processing ? 'Creating…' : 'Create lot'}
      </Button>
    {/snippet}
  </SidePanel>
</div>
