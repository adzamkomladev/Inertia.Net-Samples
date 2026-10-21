import { Link, useForm } from '@inertiajs/react'
import { ImagePlus, Loader2, X } from 'lucide-react'
import { useRef, useState, type ChangeEvent, type DragEvent, type FormEvent } from 'react'
import { toast } from 'sonner'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { uploadImage } from '@/lib/upload'
import type { PostFormData } from '@/types/posts'
import Markdown from './Markdown'

interface Props {
  initial: PostFormData
  method: 'post' | 'put'
  action: string
  submitLabel: string
  cancelHref: string
}

function FieldError({ id, message }: { id: string; message?: string }) {
  return message ? <p id={id} role="alert" className="text-sm text-destructive">{message}</p> : null
}

export default function PostForm({ initial, method, action, submitLabel, cancelHref }: Props) {
  const form = useForm<PostFormData>({ ...initial, topics: initial.topics ?? [], isPublished: initial.isPublished ?? true })
  const [topics, setTopics] = useState((initial.topics ?? []).join(', '))
  const bodyRef = useRef<HTMLTextAreaElement>(null)
  const coverInput = useRef<HTMLInputElement>(null)
  const bodyInput = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState<'cover' | 'body' | null>(null)
  const [dragging, setDragging] = useState(false)
  const [uploadError, setUploadError] = useState('')

  const upload = async (file: File | undefined, target: 'cover' | 'body') => {
    if (!file || uploading !== null || form.processing) return
    setUploading(target)
    setUploadError('')
    try {
      const { url } = await uploadImage(file, 'post')
      if (target === 'cover') {
        form.setData('coverUrl', url)
      } else {
        const ta = bodyRef.current
        const start = ta?.selectionStart ?? form.data.body.length
        const end = ta?.selectionEnd ?? start
        const alt = file.name.replace(/\.[^.]+$/, '').replace(/[[\]]/g, '')
        const md = `![${alt}](${url})`
        form.setData('body', form.data.body.slice(0, start) + md + form.data.body.slice(end))
        requestAnimationFrame(() => {
          ta?.focus()
          ta?.setSelectionRange(start + md.length, start + md.length)
        })
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Upload failed'
      setUploadError(message)
      toast.error(message)
    } finally {
      setUploading(null)
    }
  }

  const pick = (e: ChangeEvent<HTMLInputElement>, target: 'cover' | 'body') => {
    const file = e.target.files?.[0]
    e.target.value = ''
    void upload(file, target)
  }

  const drop = (e: DragEvent) => {
    e.preventDefault()
    setDragging(false)
    void upload(e.dataTransfer.files?.[0], 'cover')
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (form.processing || uploading !== null) return
    form.submit(method, action)
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-6" noValidate>
      <div className="flex flex-col gap-2">
        {form.data.coverUrl ? (
          <div className="relative">
            <img src={form.data.coverUrl} alt="Cover preview" className="aspect-[16/9] w-full rounded-lg bg-muted object-cover" />
            <Button type="button" size="icon-sm" variant="secondary" className="absolute top-2 right-2 rounded-full"
              aria-label="Remove cover image" onClick={() => form.setData('coverUrl', null)}>
              <X />
            </Button>
          </div>
        ) : (
          <div
            onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
            onDragLeave={(e) => { e.preventDefault(); setDragging(false) }}
            onDrop={drop}
            className={cn(
              'flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border border-dashed px-4 py-3 text-sm text-muted-foreground transition-colors',
              dragging ? 'border-brand bg-brand/5 text-foreground' : 'border-input',
            )}
          >
            {uploading === 'cover' ? <Loader2 className="size-4 animate-spin" aria-hidden /> : <ImagePlus className="size-4" strokeWidth={1.75} aria-hidden />}
            <span>{uploading === 'cover' ? 'Uploading cover…' : dragging ? 'Drop to upload' : 'Cover image (optional)'}</span>
            <Button type="button" variant="ghost" size="sm" className="ml-auto h-9 rounded-full" disabled={uploading !== null} onClick={() => coverInput.current?.click()}>
              Add image
            </Button>
          </div>
        )}
        <input ref={coverInput} type="file" accept="image/*" hidden aria-label="Cover image" onChange={(e) => pick(e, 'cover')} />
        <FieldError id="cover-error" message={form.errors.coverUrl} />
        {uploadError && <p role="alert" className="text-sm text-destructive">{uploadError}</p>}
        {uploading === 'body' && <p role="status" className="text-sm text-muted-foreground">Uploading image…</p>}
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="title" className="sr-only">Title</Label>
        <Input id="title" value={form.data.title} maxLength={200} required placeholder="Title" autoFocus aria-invalid={!!form.errors.title}
          aria-describedby="title-error" onChange={(e) => form.setData('title', e.target.value)}
          className="h-auto rounded-none border-0 bg-transparent px-0 py-1 font-serif text-4xl leading-tight font-semibold tracking-[-0.025em] shadow-none placeholder:text-muted-foreground/40 focus-visible:ring-0 sm:text-5xl md:text-5xl dark:bg-transparent" />
        <FieldError id="title-error" message={form.errors.title} />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="topics" className="text-muted-foreground">Topics</Label>
        <Input id="topics" value={topics} placeholder="writing, books, travel" aria-invalid={!!form.errors.topics}
          aria-describedby="topics-hint topics-error"
          onChange={e => { setTopics(e.target.value); form.setData('topics', e.target.value.split(',').map(x => x.trim()).filter(Boolean)) }} />
        <p id="topics-hint" className="text-sm text-muted-foreground">Separated by commas, up to five.</p>
        <FieldError id="topics-error" message={form.errors.topics} />
      </div>

      <div className="flex flex-col gap-2">
        <Tabs defaultValue="write">
          <div className="flex items-center justify-between gap-2 border-b">
            <TabsList variant="line" className="-mb-px">
              <TabsTrigger value="write">Write</TabsTrigger>
              <TabsTrigger value="preview">Preview</TabsTrigger>
            </TabsList>
            <Button type="button" variant="ghost" size="sm" className="rounded-full text-muted-foreground" disabled={uploading !== null} onClick={() => bodyInput.current?.click()}>
              {uploading === 'body' ? <Loader2 className="animate-spin" /> : <ImagePlus />} Insert image
            </Button>
            <input ref={bodyInput} type="file" accept="image/*" hidden aria-label="Insert image" onChange={(e) => pick(e, 'body')} />
          </div>
          <TabsContent value="write" className="pt-4">
            <Label htmlFor="body" className="sr-only">Body (Markdown supported)</Label>
            <Textarea id="body" ref={bodyRef} className="min-h-[28rem] resize-y rounded-none border-0 bg-transparent px-0 font-serif text-xl leading-[1.7] shadow-none placeholder:text-muted-foreground/40 focus-visible:ring-0 md:text-xl dark:bg-transparent" value={form.data.body} required
              placeholder="Tell the story. Markdown works here." aria-invalid={!!form.errors.body} aria-describedby="body-error"
              onChange={(e) => form.setData('body', e.target.value)} />
          </TabsContent>
          <TabsContent value="preview" className="min-h-[28rem] pt-6">
            {form.data.body.trim() ? <><h1 className="mb-8 font-serif text-4xl leading-tight font-semibold tracking-[-0.025em]">{form.data.title || 'Untitled'}</h1><Markdown>{form.data.body}</Markdown></> : <p className="text-sm text-muted-foreground">Nothing to preview yet.</p>}
          </TabsContent>
        </Tabs>
        <FieldError id="body-error" message={form.errors.body} />
      </div>

      <div className="flex items-center justify-between gap-4 border-t pt-5">
        <div className="space-y-0.5">
          <Label htmlFor="publish">Publish this post</Label>
          <p id="publish-hint" className="text-sm text-muted-foreground">{method === 'put' && initial.isPublished !== false
            ? 'Published posts can be hidden with Archive. Their publication date stays the same.'
            : 'Turn off to save a private draft. Publishing makes it visible to everyone.'}</p>
        </div>
        <Switch id="publish" checked={form.data.isPublished ?? true} aria-describedby="publish-hint"
          disabled={method === 'put' && initial.isPublished !== false}
          onCheckedChange={(v) => form.setData('isPublished', v)} />
      </div>
      <FieldError id="publish-error" message={form.errors.isPublished} />

      <div className="sticky bottom-[calc(3.5rem+env(safe-area-inset-bottom))] z-10 -mx-4 flex justify-end gap-2 border-t bg-background/90 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6 lg:bottom-0">
        <Button asChild variant="ghost" className="h-10 rounded-full"><Link href={cancelHref}>Cancel</Link></Button>
        <Button type="submit" className="h-10 rounded-full px-5 active:scale-[0.98]" disabled={form.processing || uploading !== null}>
          {form.processing && <Loader2 className="animate-spin" aria-hidden />}
          {form.data.isPublished === false ? 'Save draft' : submitLabel}
        </Button>
      </div>
    </form>
  )
}
