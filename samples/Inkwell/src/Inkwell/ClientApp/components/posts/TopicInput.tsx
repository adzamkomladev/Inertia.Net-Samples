import { X } from 'lucide-react'
import type { KeyboardEvent } from 'react'
import { cn } from 'cn'

export const MAX_TOPICS = 5

/** Same rule as the server: 1–32 lowercase letters, numbers and single inner hyphens. Truncates before trimming hyphens. */
export function normalizeTopic(raw: string) {
  return raw.trim().toLowerCase().replace(/[^a-z0-9-]+/g, '-').replace(/-{2,}/g, '-').slice(0, 32).replace(/^-+|-+$/g, '')
}

/** The topic list with a typed-but-uncommitted draft added, if it is new and there is room. */
export function withDraft(topics: string[], draft: string) {
  const topic = normalizeTopic(draft)
  return topic && !topics.includes(topic) && topics.length < MAX_TOPICS ? [...topics, topic] : topics
}

/**
 * Chip input for up to five topics: Enter, comma or Tab adds, Backspace on an empty field removes the last chip.
 * The draft text is owned by the parent so a keyboard save can include a topic that was typed but not yet added.
 */
export default function TopicInput({ id, value, onChange, draft, onDraftChange, invalid, describedBy }: {
  id: string
  value: string[]
  onChange: (topics: string[]) => void
  draft: string
  onDraftChange: (draft: string) => void
  invalid?: boolean
  describedBy?: string
}) {
  const full = value.length >= MAX_TOPICS

  const commit = () => {
    onDraftChange('')
    const next = withDraft(value, draft)
    if (next !== value) onChange(next)
  }
  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if ((e.key === 'Enter' || e.key === ',' || (e.key === 'Tab' && draft)) && draft.trim()) {
      e.preventDefault()
      commit()
    } else if (e.key === 'Enter') {
      e.preventDefault()
    } else if (e.key === 'Backspace' && !draft && value.length) {
      onChange(value.slice(0, -1))
    }
  }

  return (
    <div
      className={cn(
        'flex min-h-11 flex-wrap items-center gap-1.5 rounded-xl border border-input bg-transparent px-2 py-1.5 transition-[border-color,box-shadow] focus-within:border-ring focus-within:ring-[3px] focus-within:ring-ring/30',
        invalid && 'border-destructive ring-destructive/20',
      )}
      onClick={e => (e.currentTarget.querySelector('input') as HTMLInputElement | null)?.focus()}
    >
      {value.map(topic => (
        <span key={topic} className="inline-flex animate-pop items-center gap-1 rounded-full bg-muted py-1 pr-1 pl-2.5 text-sm">
          {topic}
          <button
            type="button" aria-label={`Remove ${topic}`} onClick={() => onChange(value.filter(t => t !== topic))}
            className="flex size-5 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-foreground hover:text-background"
          >
            <X className="size-3" aria-hidden />
          </button>
        </span>
      ))}
      <input
        id={id} value={draft} disabled={full} onChange={e => onDraftChange(e.target.value.replace(',', ''))} onKeyDown={onKeyDown} onBlur={commit}
        placeholder={full ? 'Five topics added' : value.length ? 'Add another…' : 'Add a topic, then press Enter'}
        aria-invalid={invalid} aria-describedby={describedBy}
        className="h-8 min-w-36 flex-1 bg-transparent px-1.5 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed"
      />
      <span className="px-1.5 text-xs text-muted-foreground tabular-nums" aria-hidden>{value.length}/{MAX_TOPICS}</span>
    </div>
  )
}
