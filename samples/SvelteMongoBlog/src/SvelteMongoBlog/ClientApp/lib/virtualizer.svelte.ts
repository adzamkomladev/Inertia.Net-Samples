import {
  Virtualizer,
  elementScroll,
  observeElementOffset,
  observeElementRect,
  observeWindowOffset,
  observeWindowRect,
  windowScroll,
  type VirtualizerOptions,
} from '@tanstack/virtual-core'
import { onMount, untrack } from 'svelte'

type WindowOptions<TItem extends Element> = Omit<
  VirtualizerOptions<Window, TItem>,
  'getScrollElement' | 'observeElementRect' | 'observeElementOffset' | 'scrollToFn' | 'initialOffset' | 'onChange'
>
type ElementOptions<TScroll extends Element, TItem extends Element> = Omit<
  VirtualizerOptions<TScroll, TItem>,
  'observeElementRect' | 'observeElementOffset' | 'scrollToFn' | 'onChange'
>

/**
 * Rune-native wrapper over the framework-agnostic `@tanstack/virtual-core` Virtualizer.
 * The official Svelte adapter exposes a store whose `setOptions` re-notifies subscribers, which fights
 * rune effects; this wrapper keeps an explicit version counter instead and re-reads options reactively.
 */
function create<TScroll extends Element | Window, TItem extends Element>(
  base: VirtualizerOptions<TScroll, TItem>,
  options: () => Partial<VirtualizerOptions<TScroll, TItem>>,
) {
  let version = $state(0)
  // The increment must not read `version` reactively inside the effect: that would make the effect depend on its own write.
  const bump = () => { version = untrack(() => version) + 1 }
  const onChange = () => bump()
  const instance = new Virtualizer<TScroll, TItem>({ ...base, ...options(), onChange })

  onMount(() => instance._didMount())

  $effect(() => {
    instance.setOptions({ ...base, ...options(), onChange })
    instance._willUpdate()
    bump()
  })

  return {
    measureElement: (node: HTMLElement) => instance.measureElement(node as unknown as TItem),
    get totalSize() { version; return instance.getTotalSize() },
    get virtualItems() { version; return instance.getVirtualItems() },
  }
}

export function createWindowVirtualizer<TItem extends Element>(options: () => WindowOptions<TItem>) {
  return create<Window, TItem>(
    {
      getScrollElement: () => (typeof document !== 'undefined' ? window : null),
      observeElementRect: observeWindowRect,
      observeElementOffset: observeWindowOffset,
      scrollToFn: windowScroll,
      initialOffset: () => (typeof document !== 'undefined' ? window.scrollY : 0),
    } as VirtualizerOptions<Window, TItem>,
    options,
  )
}

export function createElementVirtualizer<TScroll extends Element, TItem extends Element>(options: () => ElementOptions<TScroll, TItem>) {
  return create<TScroll, TItem>(
    { observeElementRect, observeElementOffset, scrollToFn: elementScroll } as VirtualizerOptions<TScroll, TItem>,
    options,
  )
}
