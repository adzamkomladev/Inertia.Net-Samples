type Entity = Record<string, any> & { id: string }
type Patch = (entity: Entity) => Entity

// The server answers a refused write (e.g. rate limited) with a redirect plus an error flash: that is not a success.
export const refused = (page: { flash?: unknown }) => (page.flash as { toast?: { type?: string } } | undefined)?.toast?.type === 'error'

const same = (a: unknown, b: unknown) => String(a) === String(b)
const patchList = (list: unknown, id: string, patch: Patch) =>
  Array.isArray(list) ? list.map((item: Entity) => (same(item.id, id) ? patch(item) : item)) : list

/** Builds the confirmed-state patch for a toggle. A live counter event may already have counted the change, so only a count still at the baseline is adjusted. */
export function socialPatch({ field, next, base, countField }: { field: string; next: boolean; base: number; countField?: string }): Patch {
  return entity => ({
    ...entity,
    [field]: next,
    ...(countField && Number(entity[countField] ?? 0) === base ? { [countField]: Math.max(0, base + (next ? 1 : -1)) } : {}),
  })
}

/** Rewrites one post or user wherever a page's props hold it, without asking the server for the list again. */
export function patchEntityProps(
  old: Record<string, any>,
  { kind, id, patch, extra, drop }: { kind: 'post' | 'user'; id: string; patch: Patch; extra?: Record<string, unknown>; drop?: (props: Record<string, any>) => boolean },
) {
  const list = `${kind}s`
  const next: Record<string, any> = { ...old }
  for (const [key, value] of Object.entries(extra ?? {})) if (key in old) next[key] = value
  if (old[list]?.data) {
    const data = drop?.(old) ? old[list].data.filter((item: Entity) => !same(item.id, id)) : patchList(old[list].data, id, patch)
    next[list] = { ...old[list], data }
  }
  if (old[kind] && same(old[kind].id, id)) next[kind] = patch(old[kind])
  return next
}
