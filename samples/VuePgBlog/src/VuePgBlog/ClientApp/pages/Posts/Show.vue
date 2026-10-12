<script setup lang="ts">
import { computed, ref } from 'vue'
import { useTextareaAutosize } from '@vueuse/core'
import { Deferred, Head, Link, router, useForm, usePage } from '@inertiajs/vue3'
import { BarChart3, Loader2, Pencil, Trash2 } from '@lucide/vue'
import PostActionBar from '@/components/PostActionBar.vue'
import CommentList from '@/components/CommentList.vue'
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { renderMarkdown } from '@/lib/markdown'
import { useViewBeacon } from '@/composables/useViewBeacon'
import type { CommentItem, PostDetail } from '@/types'

const props = defineProps<{ post: PostDetail; comments?: CommentItem[] }>()

const user = computed(() => usePage().props.auth.user)
const html = computed(() => renderMarkdown(props.post.body))
const date = computed(() => new Intl.DateTimeFormat('en', { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(props.post.createdAt)))
const readMinutes = computed(() => Math.max(1, Math.round(props.post.body.trim().split(/\s+/).length / 230)))
const initials = (name: string) => name.split(/\s+/).map((p) => p[0]).slice(0, 2).join('').toUpperCase()
// Count the view after mount (not on GET) so prefetched/cached visits are counted too; analytics must never break reading.
useViewBeacon(() => props.post.id)
const remove = () => router.delete(`/posts/${props.post.id}`)

const form = useForm({ body: '' })
const field = ref<InstanceType<typeof Textarea> | null>(null)
useTextareaAutosize({
  element: computed(() => field.value?.$el as HTMLTextAreaElement | undefined),
  input: computed({ get: () => form.body, set: (v) => { form.body = v } }),
  maxHeight: 240,
})
const comment = () =>
  form.post(`/posts/${props.post.id}/comments`, { preserveScroll: true, preserveState: true, only: ['post', 'comments'], onSuccess: () => form.reset() })
</script>

<template>
  <Head :title="post.title">
    <meta name="description" :content="post.excerpt" />
  </Head>
  <article class="animate-rise mx-auto flex max-w-2xl flex-col gap-8">
    <header class="flex flex-col gap-5">
      <img v-if="post.coverUrl" :src="post.coverUrl" :alt="`Cover image for ${post.title}`" class="bg-muted aspect-[16/9] w-full rounded-xl border object-cover" />

      <h1 class="font-serif text-4xl leading-tight font-semibold tracking-tight text-balance sm:text-[2.6rem]">{{ post.title }}</h1>

      <div class="flex flex-wrap items-center gap-x-3 gap-y-3">
        <Link :href="`/users/${post.author.id}`" class="focus-visible:ring-ring shrink-0 rounded-full outline-none focus-visible:ring-2">
          <Avatar class="size-10">
            <AvatarImage v-if="post.author.avatarUrl" :src="post.author.avatarUrl" alt="" />
            <AvatarFallback class="text-xs font-medium">{{ initials(post.author.name) }}</AvatarFallback>
          </Avatar>
        </Link>
        <div class="flex min-w-0 flex-col text-sm">
          <Link :href="`/users/${post.author.id}`" class="truncate font-medium hover:underline">{{ post.author.name }}</Link>
          <p class="text-muted-foreground">
            <time :datetime="post.createdAt">{{ date }}</time>
            <span aria-hidden="true"> · </span>{{ readMinutes }} min read
            <span aria-hidden="true"> · </span><span class="tabular-nums">{{ post.viewCount }}</span> views
          </p>
        </div>

        <div v-if="post.canEdit" class="ml-auto flex items-center gap-1">
          <Button as-child variant="ghost" size="sm" class="text-muted-foreground rounded-full"><Link :href="`/posts/${post.slug}/edit`"><Pencil /> Edit</Link></Button>
          <Button as-child variant="ghost" size="sm" class="text-muted-foreground rounded-full"><Link :href="`/analytics/posts/${post.id}`"><BarChart3 /> Analytics</Link></Button>
          <AlertDialog>
            <AlertDialogTrigger as-child>
              <Button variant="ghost" size="sm" class="text-muted-foreground hover:bg-destructive/10 hover:text-destructive rounded-full"><Trash2 /> Delete</Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Delete this post?</AlertDialogTitle>
                <AlertDialogDescription>This permanently removes the post and its comments.</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction @click="remove">Delete</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
      </div>

      <p v-if="post.archivedAt" class="bg-muted text-muted-foreground rounded-lg px-3 py-2 text-sm">This post is archived and only visible to you.</p>
    </header>

    <div class="prose-post break-words" v-html="html" />

    <div v-if="post.topics?.length" class="flex flex-wrap gap-x-3 gap-y-1">
      <span v-for="topic in post.topics" :key="topic" class="text-muted-foreground text-sm">#{{ topic }}</span>
    </div>

    <div class="border-y py-1.5">
      <PostActionBar :post="post" />
    </div>

    <section id="comments" aria-labelledby="comments-heading" class="flex scroll-mt-20 flex-col gap-5">
      <h2 id="comments-heading" class="text-base font-semibold">Comments <span class="text-muted-foreground font-normal tabular-nums">{{ post.commentCount }}</span></h2>

      <p v-if="post.archivedAt" class="text-muted-foreground text-sm">Comments are closed while this post is archived.</p>
      <form v-else-if="user?.verified" class="flex gap-3" novalidate @submit.prevent="comment">
        <Avatar class="mt-0.5 size-9">
          <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" alt="" />
          <AvatarFallback class="text-xs">{{ initials(user.name) }}</AvatarFallback>
        </Avatar>
        <div class="flex min-w-0 flex-1 flex-col gap-2">
          <Textarea
            ref="field" v-model="form.body" aria-label="Add a comment" placeholder="Add to the conversation" rows="2" maxlength="5000"
            :aria-invalid="!!form.errors.body" class="field-sizing-fixed max-h-60 min-h-20 resize-none rounded-xl text-[15px]"
          />
          <p v-if="form.errors.body" role="alert" class="text-destructive text-sm">{{ form.errors.body }}</p>
          <div class="flex justify-end">
            <Button type="submit" size="sm" class="rounded-full active:scale-[0.98]" :disabled="form.processing || !form.body.trim()">
              <Loader2 v-if="form.processing" class="animate-spin" /> Reply
            </Button>
          </div>
        </div>
      </form>
      <p v-else-if="user" class="text-muted-foreground text-sm">
        Verify your email to join the conversation. <Link href="/email/verify" class="text-brand underline underline-offset-4">Verify now</Link>
      </p>
      <p v-else class="text-muted-foreground text-sm">
        <Link href="/login" class="text-brand underline underline-offset-4">Log in</Link> to join the conversation.
      </p>

      <Deferred data="comments">
        <template #fallback>
          <div class="divide-y" aria-busy="true">
            <div v-for="i in 3" :key="i" class="flex gap-3 py-4">
              <Skeleton class="size-9 shrink-0 rounded-full" />
              <div class="flex-1 space-y-2">
                <Skeleton class="h-3.5 w-32" />
                <Skeleton class="h-3.5 w-full" />
                <Skeleton class="h-3.5 w-2/3" />
              </div>
            </div>
          </div>
        </template>
        <CommentList :key="post.id" :post-id="post.id" :comments="comments ?? []" :archived="!!post.archivedAt" />
      </Deferred>
    </section>
  </article>
</template>
