<script setup lang="ts">
import { Ellipsis, Archive, ArchiveRestore } from '@lucide/vue'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Button } from '@/components/ui/button'
import { useSimpleToggle } from '@/composables/useSocialToggle'
import type { PostSummary } from '@/types'

const props = defineProps<{ post: PostSummary }>()
const archive = useSimpleToggle(() => `/posts/${props.post.id}/archive`, () => !!props.post.archivedAt)
</script>

<template>
  <DropdownMenu v-if="post.canArchive">
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon-sm" class="text-muted-foreground size-8 rounded-full" aria-label="More post actions">
        <Ellipsis />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem :disabled="archive.pending.value" @select="archive.toggle">
        <ArchiveRestore v-if="post.archivedAt" />
        <Archive v-else />
        {{ post.archivedAt ? 'Restore post' : 'Archive post' }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
