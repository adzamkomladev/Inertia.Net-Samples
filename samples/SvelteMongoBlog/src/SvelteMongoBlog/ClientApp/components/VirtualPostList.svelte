<script lang="ts">
  import { InfiniteScroll } from '@inertiajs/svelte'
  import type { ReloadOptions } from '@inertiajs/core'
  import { onMount } from 'svelte'
  import LoadingRows from '@/components/LoadingRows.svelte'
  import PostCard from '@/components/PostCard.svelte'
  import { createWindowVirtualizer } from '@/lib/virtualizer.svelte'
  import type { PostSummary, ScrollPage } from '@/types'

  let { posts, params }: { posts: ScrollPage<PostSummary>; params?: ReloadOptions } = $props()

  let list: HTMLElement | undefined = $state()
  let end: HTMLElement | undefined = $state()
  let margin = $state(0)
  let mounted = $state(false)
  onMount(() => {
    mounted = true
    if (list) margin = list.getBoundingClientRect().top + window.scrollY
  })

  const virtualizer = createWindowVirtualizer<HTMLElement>(() => ({
    count: posts.data.length,
    estimateSize: () => 360,
    overscan: 5,
    getItemKey: (index: number) => posts.data[index]!.id,
    scrollMargin: margin,
  }))

  // Rows from later pages ease in (staggered) the first time they appear; rows present on first render never animate.
  const fresh = new Map<string, number>()
  const done = new Set<string>()
  // svelte-ignore state_referenced_locally
  let lastLength = posts.data.length
  $effect.pre(() => {
    const length = posts.data.length
    if (length > lastLength) posts.data.slice(lastLength).forEach((post, i) => fresh.set(String(post.id), Math.min(i, 6) * 60))
    lastLength = length
  })
  function rise(id: string) {
    const key = String(id)
    const delay = done.has(key) ? undefined : fresh.get(key)
    return { class: delay === undefined ? undefined : 'animate-rise', style: delay ? `animation-delay:${delay}ms` : undefined }
  }
  const markDone = (event: AnimationEvent, id: string) => { if (event.target === event.currentTarget) done.add(String(id)) }
  const measure = (node: HTMLElement) => virtualizer.measureElement(node)
</script>

<InfiniteScroll data="posts" {params} onlyNext preserveUrl buffer={600} endElement={() => end ?? null}>
  <div bind:this={list} role="list" aria-label="Posts" style={mounted ? `height:${virtualizer.totalSize}px;position:relative` : undefined}>
    {#if mounted}
      {#each virtualizer.virtualItems as row (row.key)}
        {@const post = posts.data[row.index]!}
        {@const animate = rise(post.id)}
        <div data-index={row.index} {@attach measure} role="listitem" class="absolute top-0 left-0 w-full" style={`transform:translateY(${row.start - margin}px)`}>
          <div class={animate.class} style={animate.style} onanimationend={(event) => markDone(event, post.id)}>
            <PostCard {post} />
          </div>
        </div>
      {/each}
    {:else}
      {#each posts.data as post (post.id)}
        {@const animate = rise(post.id)}
        <div role="listitem" class={animate.class} style={animate.style} onanimationend={(event) => markDone(event, post.id)}>
          <PostCard {post} />
        </div>
      {/each}
    {/if}
  </div>
  <div bind:this={end} class="h-px" aria-hidden="true"></div>
  {#snippet loading()}<LoadingRows kind="posts" />{/snippet}
</InfiniteScroll>
