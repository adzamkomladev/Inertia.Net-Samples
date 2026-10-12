/** Independent publishers send invalidations; persisted page data is authoritative. */
export type CounterEvent = { id: string; sequence?: number; likeCount?: number; commentCount?: number; viewCount?: number; repostCount?: number }

export function refreshProps(props: Record<string, any>, ids: Iterable<string>): string[] {
  const changed = new Set([...ids].map(String))
  const only: string[] = []
  if (props.posts?.data?.some((p: any) => changed.has(String(p.id)))) only.push('posts')
  if (props.post && changed.has(String(props.post.id))) only.push('post')
  return only
}
