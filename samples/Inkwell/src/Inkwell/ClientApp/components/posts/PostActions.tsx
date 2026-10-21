import { Ellipsis, Archive, ArchiveRestore } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useSimpleToggle } from '@/hooks/use-social-mutation'
import type { SocialPost } from '@/types/posts'

/** Overflow menu for owner-only post actions (archive / restore). */
export default function PostActions({ post }: { post: SocialPost }) {
  const archive = useSimpleToggle(`/posts/${post.id}/archive`, !!post.archivedAt)
  if (!post.canArchive) return null
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="size-10 rounded-full text-muted-foreground" aria-label="More post actions">
          <Ellipsis />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem onSelect={archive.toggle} disabled={archive.pending}>
          {post.archivedAt ? <ArchiveRestore /> : <Archive />}
          {post.archivedAt ? 'Restore post' : 'Archive post'}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
