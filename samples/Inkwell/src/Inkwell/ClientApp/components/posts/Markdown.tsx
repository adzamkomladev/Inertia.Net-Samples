import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from 'cn'

// No rehype-raw: raw HTML in markdown is never rendered.
export default function Markdown({ children, className }: { children: string; className?: string }) {
  return (
    <div className={cn('prose-post break-words', className)}>
      <ReactMarkdown remarkPlugins={[remarkGfm]}>{children}</ReactMarkdown>
    </div>
  )
}
