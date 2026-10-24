<script lang="ts">
  import { Link, router } from '@inertiajs/svelte'
  import ArrowRight from '@lucide/svelte/icons/arrow-right'
  import ArrowUpRight from '@lucide/svelte/icons/arrow-up-right'
  import CarFront from '@lucide/svelte/icons/car-front'
  import MapPin from '@lucide/svelte/icons/map-pin'
  import Search from '@lucide/svelte/icons/search'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import Clock from '@lucide/svelte/icons/clock'
  import Plus from '@lucide/svelte/icons/plus'
  import EmptyState from '@/components/EmptyState.svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import { money } from '@/lib/money'
  import type { LotCard as LotCardType } from '@/types'
  let { lots }: { lots: LotCardType[] } = $props()
  const featured = $derived(lots.find(lot => lot.free > 0) ?? lots[0])
  const areas = $derived([...new Set(lots.map(lot => lot.area))].slice(0, 3))
  const steps = [
    { title: 'Find your spot.', body: 'Compare locations, hourly prices and live availability. Choose a bay that fits your car.', icon: MapPin },
    { title: 'Make time for your plans.', body: 'Reserve a single visit or repeat your parking weekly. See the full price before you book.', icon: Clock },
    { title: 'Arrive. Park. Get on with it.', body: 'Your booking is linked to your number plate. Show up during your window and the guard checks you in.', icon: CarFront },
  ]
  const stats = $derived([
    { label: 'Bays free right now', value: lots.reduce((sum, lot) => sum + lot.free, 0) },
    { label: 'Lots open', value: lots.length },
    { label: 'From, per hour', value: money(Math.min(...lots.map(lot => lot.ratePerHourCents))) },
  ])
  const faqs = [
    { q: 'What if my plans change?', a: 'Cancel from your bookings. 24 hours or more before your window gets a full refund, 4 hours or more gets half; later cancellations are not refunded.' },
    { q: 'Can I book the same bay every week?', a: 'Yes. Pick Weekly, choose your days and times, and reserve up to 8 weeks of visits on the same bay in one go.' },
    { q: 'How does the guard know it is me?', a: 'Your booking is tied to your number plate. Save your plates in your profile and they are one tap away at checkout.' },
    { q: 'What does Spot Cover pay for?', a: 'Damage or theft while you are parked, up to the lot’s limit. It costs 12% of the parking charge (minimum $2) and you claim from your account.' },
  ]
  const search = (event: SubmitEvent) => {
    event.preventDefault()
    const q = String(new FormData(event.currentTarget as HTMLFormElement).get('q') ?? '').trim()
    router.get('/lots', q ? { q } : {})
  }
</script>
<PageTitle title="Parking, already sorted" description="Find a parking lot, reserve a numbered bay and arrive with one less thing to think about." />
<section class="overflow-hidden border-b">
  <div class="mx-auto grid w-full max-w-6xl items-center gap-12 px-4 py-12 sm:px-6 sm:py-20 lg:grid-cols-[1.05fr_1fr] lg:gap-14">
    <div>
      <p class="text-primary mb-5 flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em]"><span class="bg-primary size-2 rounded-full"></span>A space for wherever you're going</p>
      <h1 class="max-w-xl text-5xl font-semibold leading-[1.04] tracking-[-0.055em] sm:text-7xl">Less circling.<br /><span class="text-primary">More arriving.</span></h1>
      <p class="text-muted-foreground mt-6 max-w-md text-lg leading-8">Your day has better things in it than looking for parking. Reserve a numbered bay before you leave.</p>
      <form role="search" onsubmit={search} class="bg-card mt-8 rounded-2xl border p-3 shadow-lifted">
        <label for="home-search" class="text-muted-foreground mb-2 block px-2 text-xs font-semibold uppercase tracking-wide">Where are you heading?</label>
        <div class="flex flex-col gap-3 sm:flex-row">
          <div class="relative min-w-0 flex-1"><Search class="text-muted-foreground pointer-events-none absolute left-3 top-1/2 size-5 -translate-y-1/2" aria-hidden="true" /><input id="home-search" name="q" type="search" placeholder="Area, address or lot name" class="bg-muted placeholder:text-muted-foreground focus-visible:ring-ring h-12 w-full rounded-xl pl-11 pr-3 outline-none focus-visible:ring-2" /></div>
          <button type="submit" class="bg-primary text-primary-foreground hover:bg-primary/90 inline-flex h-12 shrink-0 transition-transform active:scale-[0.98] [&:hover_svg]:translate-x-0.5 [&_svg]:transition-transform items-center justify-center gap-2 rounded-xl px-5 text-sm font-semibold">Find parking <ArrowRight class="size-4" aria-hidden="true" /></button>
        </div>
      </form>
      {#if areas.length}<div class="text-muted-foreground mt-4 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs"><span>Explore</span>{#each areas as area}<Link href={`/lots?q=${encodeURIComponent(area)}`} class="text-foreground hover:text-primary inline-flex min-h-9 items-center gap-1 font-medium">{area}<ArrowUpRight class="size-3" aria-hidden="true" /></Link>{/each}</div>{/if}
    </div>
    <div class="parking-preview relative rounded-[2rem] bg-slate-950 p-5 text-slate-100 sm:p-8">
      <div class="flex items-center justify-between gap-3"><p class="text-xs font-medium uppercase tracking-[0.2em] text-slate-400">One less thing to think about</p><span class="rounded-full border border-white/15 px-3 py-1 text-xs text-teal-200">Your arrival, sorted</span></div>
      <div class="my-8 rounded-2xl border border-white/10 bg-white/5 p-4 sm:p-6" aria-hidden="true">
        <div class="mb-5 flex justify-between text-[10px] uppercase tracking-[0.2em] text-slate-400"><span>Level 01 / Section A</span><span>Entry →</span></div>
        <div class="grid grid-cols-4 gap-2 sm:gap-3">{#each [1,2,3,4] as bay}<div class={`relative grid h-20 place-items-center rounded-lg border-2 ${bay === 3 ? 'reserved border-teal-300 bg-teal-300/15 text-teal-100' : 'border-white/15 text-slate-500'}`}><span class="font-mono text-xl">A–0{bay}</span>{#if bay === 3}<span class="absolute -top-2.5 rounded-full bg-teal-300 px-2 text-[10px] font-bold tracking-wide text-slate-950 uppercase">Yours</span>{/if}</div>{/each}</div>
        <div class="relative my-5 flex items-center gap-3 overflow-hidden"><div class="flex-1 border-t border-dashed border-white/20"></div><span class="animate-drive inline-flex items-center gap-2"><CarFront class="size-7 text-amber-200" /><ArrowRight class="size-4 text-slate-400" /></span><div class="flex-1 border-t border-dashed border-white/20"></div></div>
        <div class="grid grid-cols-4 gap-2 sm:gap-3">{#each [5,6,7,8] as bay}<div class="grid h-20 place-items-center rounded-lg border-2 border-white/15 text-slate-500"><span class="font-mono text-xl">A–0{bay}</span></div>{/each}</div>
      </div>
      {#if featured}
        <Link href={`/lots/${featured.id}`} class="group block rounded-2xl bg-white p-4 text-slate-950 sm:p-5">
          <div class="flex items-start justify-between gap-4"><div class="min-w-0"><p class="mb-1 flex items-center gap-1.5 text-xs font-medium text-slate-500"><span class="size-1.5 rounded-full bg-emerald-500"></span>Live availability · Featured lot</p><h2 class="text-lg font-semibold leading-6">{featured.name}</h2></div><ArrowUpRight class="size-5 shrink-0 transition-transform group-hover:-translate-y-0.5 group-hover:translate-x-0.5" /></div>
          <div class="mt-4 flex flex-wrap items-end justify-between gap-3 border-t border-slate-100 pt-4"><p class="text-sm"><strong class="text-emerald-700">{featured.free} bays free</strong><span class="ml-2 text-slate-500">{featured.area}</span></p><p class="text-lg font-semibold">{money(featured.ratePerHourCents)}<span class="ml-1 text-xs font-normal text-slate-500">/ hour</span></p></div>
        </Link>
      {:else}<div class="rounded-2xl border border-white/15 p-5"><p class="font-semibold">A clearer way to park.</p><p class="mt-2 text-sm leading-6 text-slate-400">Choose a location, a bay and your hours. We'll show live availability when lots open.</p></div>{/if}
      <p class="mt-5 text-center text-xs text-slate-400">Illustrative bay layout. Open a lot to see its live floor plan.</p>
    </div>
  </div>
</section>
{#if lots.length}
  <section class="mx-auto max-w-6xl px-4 pt-10 sm:px-6" aria-label="Right now on CarParker">
    <dl class="bg-card shadow-card grid grid-cols-3 divide-x rounded-2xl ring-1 ring-foreground/5">
      {#each stats as stat (stat.label)}
        <div class="px-4 py-5 text-center sm:px-6">
          <dt class="text-muted-foreground text-xs font-medium sm:text-sm">{stat.label}</dt>
          <dd class="font-display mt-1 text-2xl font-bold tabular-nums sm:text-4xl">{stat.value}</dd>
        </div>
      {/each}
    </dl>
  </section>
{/if}
<section class="mx-auto max-w-6xl px-4 py-10 sm:px-6" aria-label="Parking made simple">
  <div class="grid gap-5 border-b pb-10 sm:grid-cols-3">{#each [{ icon: MapPin, title: 'A bay, not a guess', body: 'Pick your space on the live lot map.' }, { icon: Clock, title: 'Your hours. Your routine.', body: 'One visit or the same time every week.' }, { icon: ShieldCheck, title: 'A little extra peace of mind', body: 'Add optional Spot Cover at checkout.' }] as benefit}<div class="flex gap-3"><benefit.icon class="text-primary mt-1 size-5 shrink-0" /><div><h2 class="text-sm font-semibold">{benefit.title}</h2><p class="text-muted-foreground mt-1 text-sm leading-6">{benefit.body}</p></div></div>{/each}</div>
</section>
<section class="mx-auto max-w-6xl px-4 pt-4 sm:px-6" aria-labelledby="lots-title">
  <div class="mb-6 flex flex-wrap items-end justify-between gap-4"><div><p class="text-primary mb-2 text-xs font-semibold uppercase tracking-widest">Find your next stop</p><h2 id="lots-title" class="text-3xl font-semibold tracking-tight">Good places to leave your car.</h2><p class="text-muted-foreground mt-2">Live availability. Clear hourly prices. A space that works for you.</p></div><Link href="/lots" class="text-primary inline-flex min-h-11 items-center gap-2 text-sm font-semibold hover:underline">Explore all lots <ArrowRight class="size-4" /></Link></div>
  {#if lots.length}<div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">{#each lots.slice(0,6) as lot (lot.id)}<LotCard {lot} />{/each}</div>{:else}<EmptyState icon={CarFront} title="No lots are open yet" description="Lots appear here as soon as they open for bookings." />{/if}
</section>
<section class="mx-auto max-w-6xl px-4 pt-20 sm:px-6" aria-labelledby="how-title">
  <div class="bg-card rounded-[2rem] border p-6 sm:p-10"><p class="text-primary text-xs font-semibold uppercase tracking-widest">From here to parked</p><h2 id="how-title" class="mt-3 text-3xl font-semibold tracking-tight">Three steps. Then you're on your way.</h2><ol class="mt-10 grid gap-4 sm:grid-cols-3">{#each steps as step, i}<li class="group bg-muted/50 hover:bg-muted rounded-2xl p-5 transition-colors"><div class="flex items-center justify-between"><span class="bg-primary/10 text-primary grid size-11 place-items-center rounded-xl transition-transform duration-300 group-hover:-rotate-6 group-hover:scale-110"><step.icon class="size-5" aria-hidden="true" /></span><span class="text-primary/25 text-4xl font-semibold tracking-tight">0{i+1}</span></div><h3 class="mt-5 font-semibold">{step.title}</h3><p class="text-muted-foreground mt-2 text-sm leading-7">{step.body}</p></li>{/each}</ol><div class="mt-10 flex flex-col items-start justify-between gap-5 border-t pt-6 sm:flex-row sm:items-center"><p class="text-muted-foreground max-w-xl text-sm leading-6"><ShieldCheck class="text-primary mr-1 inline size-4" />Optional Spot Cover costs 12% of parking (minimum $2) and covers damage or theft up to the lot's limit.</p><Link href="/lots" class="bg-primary text-primary-foreground inline-flex min-h-11 shrink-0 items-center gap-2 rounded-xl px-5 text-sm font-semibold">Find my space <ArrowRight class="size-4" /></Link></div></div>
</section>
<section class="mx-auto max-w-3xl px-4 pt-20 pb-6 sm:px-6" aria-labelledby="faq-title">
  <p class="text-primary text-center text-xs font-semibold uppercase tracking-widest">Good to know</p>
  <h2 id="faq-title" class="mt-3 text-center text-3xl font-semibold tracking-tight">Questions drivers ask</h2>
  <div class="mt-8 divide-y rounded-2xl border bg-card">
    {#each faqs as faq (faq.q)}
      <details class="faq group px-5">
        <summary class="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 py-4 font-semibold">
          {faq.q}
          <Plus class="text-muted-foreground size-5 shrink-0 transition-transform duration-300 group-open:rotate-45" aria-hidden="true" />
        </summary>
        <p class="text-muted-foreground pb-5 text-sm leading-7">{faq.a}</p>
      </details>
    {/each}
  </div>
</section>
<style>
  .faq summary::-webkit-details-marker { display: none; }
  .faq[open] p { animation: var(--animate-rise); }
  .reserved { animation: var(--animate-pulse-soft); }
  .parking-preview { background-image: radial-gradient(ellipse at top right, rgb(45 212 191 / 0.13), transparent 65%); }
</style>
