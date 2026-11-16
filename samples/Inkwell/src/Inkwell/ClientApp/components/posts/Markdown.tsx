import ReactMarkdown, { type Components } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from 'cn'

// The essay title is the page's only h1, so a `# Heading` in the body renders one level down.
const components: Components = { h1: ({ node: _node, ...props }) => <h2 {...props} /> }

// No rehype-raw: raw HTML in markdown is never rendered.
export default function Markdown({ children, className }: { children: string; className?: string }) {
  return (
    <div className={cn('prose-post max-w-none break-words', className)}>
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>{children}</ReactMarkdown>
    </div>
  )
}
