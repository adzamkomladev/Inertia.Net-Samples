import { Link, useForm } from '@inertiajs/react'
import { Eye, Globe, ImagePlus, Loader2, Lock, PenLine, X } from 'lucide-react'
import { useEffect, useRef, useState, type ChangeEvent, type DragEvent, type FormEvent, type KeyboardEvent } from 'react'
import { toast } from 'sonner'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { uploadImage } from '@/lib/upload'
import type { PostFormData } from '@/types/posts'
import Markdown from './Markdown'
import TopicInput, { withDraft } from './TopicInput'

interface Props {
  initial: PostFormData
  method: 'post' | 'put'
  action: string
  submitLabel: string
  cancelHref: string
}

function FieldError({ id, message }: { id: string; message?: string }) {
  return message ? <p id={id} role="alert" className="animate-rise text-sm text-destructive">{message}</p> : null
}

const words = (text: string) => text.trim() ? text.trim().split(/\s+/).length : 0

export default function PostForm({ initial, method, action, submitLabel, cancelHref }: Props) {
  const form = useForm<PostFormData>({ ...initial, topics: initial.topics ?? [], isPublished: initial.isPublished ?? true })
  const bodyRef = useRef<HTMLTextAreaElement>(null)
  const coverInput = useRef<HTMLInputElement>(null)
  const bodyInput = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState<'cover' | 'body' | null>(null)
  const [dragging, setDragging] = useState(false)
  const [uploadError, setUploadError] = useState('')
  const [topicDraft, setTopicDraft] = useState('')
  // A published post cannot go back to draft; the visibility switch is locked for it.
  const visibilityLocked = method === 'put' && initial.isPublished !== false
  const draft = form.data.isPublished === false
  const count = words(form.data.body)

  // Warn before closing the tab with unsaved writing. Inertia visits from Cancel are explicit and allowed.
  useEffect(() => {
    if (!form.isDirty || form.processing) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [form.isDirty, form.processing])

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
      toast.success(target === 'cover' ? 'Cover added' : 'Image inserted')
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

  const submit = (e?: FormEvent) => {
    e?.preventDefault()
    if (form.processing || uploading !== null) return
    // A keyboard save can happen while a topic is still typed in the field: send it and show it as a chip.
    const topics = withDraft(form.data.topics ?? [], topicDraft)
    if (topicDraft) {
      setTopicDraft('')
      form.setData('topics', topics)
    }
    form.transform(data => ({ ...data, topics }))
    form.submit(method, action, { onError: () => toast.error('Check the highlighted fields') })
  }

  // Ctrl/Cmd+S or Ctrl/Cmd+Enter saves from anywhere in the editor.
  const onKeyDown = (e: KeyboardEvent<HTMLFormElement>) => {
    if ((e.metaKey || e.ctrlKey) && (e.key === 's' || e.key === 'Enter')) {
      e.preventDefault()
      submit()
    }
  }

  return (
    <form onSubmit={submit} onKeyDown={onKeyDown} className="flex flex-col gap-7" noValidate>
      <div className="flex flex-col gap-2">
        {form.data.coverUrl ? (
          <div className="group/cover relative animate-rise overflow-hidden rounded-2xl">
            <img src={form.data.coverUrl} alt="Cover preview" className="aspect-[16/9] w-full bg-muted object-cover" />
            <div className="absolute inset-x-0 top-0 flex justify-end gap-2 bg-gradient-to-b from-black/40 to-transparent p-3 opacity-100 transition-opacity sm:opacity-0 sm:group-hover/cover:opacity-100 sm:group-focus-within/cover:opacity-100">
              <Button type="button" size="sm" variant="secondary" className="rounded-full" disabled={uploading !== null} onClick={() => coverInput.current?.click()}>
                {uploading === 'cover' ? <Loader2 className="animate-spin" /> : <ImagePlus />} Replace
              </Button>
              <Button type="button" size="icon-sm" variant="secondary" className="rounded-full" aria-label="Remove cover image" onClick={() => form.setData('coverUrl', null)}>
                <X />
              </Button>
            </div>
          </div>
        ) : (
          <button
            type="button"
            onClick={() => coverInput.current?.click()}
            disabled={uploading !== null}
            onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
            onDragLeave={(e) => { e.preventDefault(); setDragging(false) }}
            onDrop={drop}
            className={cn(
              'group/drop flex items-center gap-3 rounded-2xl border border-dashed px-4 py-3.5 text-left text-sm text-muted-foreground transition-[border-color,background-color,color]',
              dragging ? 'border-brand bg-brand/5 text-foreground' : 'border-input hover:border-foreground/30 hover:bg-muted/40 hover:text-foreground',
            )}
          >
            <span className="flex size-9 items-center justify-center rounded-full bg-muted transition-transform group-hover/drop:scale-105">
              {uploading === 'cover' ? <Loader2 className="size-4 animate-spin" aria-hidden /> : <ImagePlus className="size-4" strokeWidth={1.75} aria-hidden />}
            </span>
            <span className="flex-1">
              <span className="block font-medium text-foreground">{uploading === 'cover' ? 'Uploading cover…' : dragging ? 'Drop to upload' : 'Add a cover image'}</span>
              <span className="block text-xs">Optional. Drag an image here or click to browse.</span>
            </span>
          </button>
        )}
        <input ref={coverInput} type="file" accept="image/*" hidden aria-label="Cover image" onChange={(e) => pick(e, 'cover')} />
        <FieldError id="cover-error" message={form.errors.coverUrl} />
        {uploadError && <p role="alert" className="text-sm text-destructive">{uploadError}</p>}
      </div>

      <div className="flex flex-col gap-1">
        <Label htmlFor="title" className="sr-only">Title</Label>
        <Textarea id="title" value={form.data.title} maxLength={200} required placeholder="Title" autoFocus rows={1} aria-invalid={!!form.errors.title}
          aria-describedby="title-error"
          onChange={(e) => form.setData('title', e.target.value.replace(/\n/g, ''))}
          onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); bodyRef.current?.focus() } }}
          className="field-auto min-h-0 resize-none rounded-none border-0 bg-transparent px-0 py-1 font-serif text-4xl leading-tight font-semibold tracking-[-0.025em] shadow-none placeholder:text-muted-foreground/40 focus-visible:ring-0 aria-invalid:ring-0 sm:text-5xl md:text-5xl dark:bg-transparent" />
        <FieldError id="title-error" message={form.errors.title} />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="topics" className="text-muted-foreground">Topics</Label>
        <TopicInput id="topics" value={form.data.topics ?? []} onChange={t => form.setData('topics', t)} draft={topicDraft} onDraftChange={setTopicDraft} invalid={!!form.errors.topics} describedBy="topics-error" />
        <FieldError id="topics-error" message={form.errors.topics} />
      </div>

      <div className="flex flex-col gap-2">
        <Tabs defaultValue="write">
          <div className="flex items-center justify-between gap-2 border-b">
            <TabsList variant="line" className="-mb-px">
              <TabsTrigger value="write"><PenLine /> Write</TabsTrigger>
              <TabsTrigger value="preview"><Eye /> Preview</TabsTrigger>
            </TabsList>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button type="button" variant="ghost" size="sm" className="rounded-full text-muted-foreground" aria-label="Insert image" disabled={uploading !== null} onClick={() => bodyInput.current?.click()}>
                  {uploading === 'body' ? <Loader2 className="animate-spin" /> : <ImagePlus />} <span className="hidden sm:inline">Insert image</span>
                </Button>
              </TooltipTrigger>
              <TooltipContent>Inserts Markdown at the cursor</TooltipContent>
            </Tooltip>
            <input ref={bodyInput} type="file" accept="image/*" hidden aria-label="Insert image" onChange={(e) => pick(e, 'body')} />
          </div>
          <TabsContent value="write" className="pt-4">
            <Label htmlFor="body" className="sr-only">Body (Markdown supported)</Label>
            <Textarea id="body" ref={bodyRef} className="field-auto min-h-[24rem] resize-none rounded-none border-0 bg-transparent px-0 font-serif text-xl leading-[1.7] shadow-none placeholder:text-muted-foreground/40 focus-visible:ring-0 aria-invalid:ring-0 md:text-xl dark:bg-transparent" value={form.data.body} required
              placeholder="Tell the story. Markdown works here: # headings, **bold**, > quotes, lists and links." aria-invalid={!!form.errors.body} aria-describedby="body-error"
              onChange={(e) => form.setData('body', e.target.value)} />
          </TabsContent>
          <TabsContent value="preview" className="min-h-[24rem] pt-6">
            {form.data.body.trim() ? <><h1 className="mb-8 font-serif text-4xl leading-tight font-semibold tracking-[-0.025em]">{form.data.title || 'Untitled'}</h1><Markdown>{form.data.body}</Markdown></> : <p className="text-sm text-muted-foreground">Nothing to preview yet.</p>}
          </TabsContent>
        </Tabs>
        <FieldError id="body-error" message={form.errors.body} />
        <FieldError id="publish-error" message={form.errors.isPublished} />
      </div>

      <div className="sticky bottom-[calc(3.5rem+env(safe-area-inset-bottom))] z-10 -mx-4 flex flex-wrap items-center gap-x-3 gap-y-2 border-t bg-background/90 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6 lg:bottom-0">
        <RadioGroup
          aria-label="Visibility" value={draft ? 'draft' : 'public'} onValueChange={v => form.setData('isPublished', v === 'public')}
          className="inline-flex gap-0 rounded-full bg-muted p-1"
        >
          {([
            { value: 'public', label: 'Public', icon: Globe },
            { value: 'draft', label: 'Draft', icon: Lock },
          ] as const).map(option => {
            const locked = visibilityLocked && option.value === 'draft'
            return (
              <Label
                key={option.value} htmlFor={`visibility-${option.value}`}
                title={locked ? 'Published posts can be archived instead' : undefined}
                className={cn(
                  'flex h-8 cursor-pointer items-center gap-1.5 rounded-full px-3 text-sm font-normal text-muted-foreground transition-[background-color,color,box-shadow] hover:text-foreground',
                  'has-[[data-state=checked]]:bg-background has-[[data-state=checked]]:font-medium has-[[data-state=checked]]:text-foreground has-[[data-state=checked]]:shadow-sm',
                  'has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring has-[:disabled]:cursor-not-allowed has-[:disabled]:opacity-40',
                )}
              >
                <RadioGroupItem id={`visibility-${option.value}`} value={option.value} disabled={locked} aria-label={option.label} className="sr-only" />
                <option.icon className="size-3.5" aria-hidden /> <span className="sr-only sm:not-sr-only">{option.label}</span>
              </Label>
            )
          })}
        </RadioGroup>
        <p className="hidden text-xs text-muted-foreground tabular-nums sm:block" aria-live="polite">
          {count ? `${count.toLocaleString()} ${count === 1 ? 'word' : 'words'} · ${Math.max(1, Math.round(count / 230))} min read` : 'Start writing'}
        </p>
        <div className="ml-auto flex items-center gap-2">
          <Button asChild variant="ghost" className="h-10 rounded-full"><Link href={cancelHref}>Cancel</Link></Button>
          <Button type="submit" className="h-10 min-w-28 rounded-full px-5 active:scale-[0.98]" disabled={form.processing || uploading !== null}>
            {form.processing && <Loader2 className="animate-spin" aria-hidden />}
            {form.processing ? 'Saving…' : draft ? 'Save draft' : submitLabel}
          </Button>
        </div>
      </div>
    </form>
  )
}
