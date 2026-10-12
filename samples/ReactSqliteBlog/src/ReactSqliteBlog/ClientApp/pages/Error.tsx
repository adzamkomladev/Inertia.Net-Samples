import { Link } from '@inertiajs/react'
import { Button } from '@/components/ui/button'
import AppLayout from '@/layouts/AppLayout'

const messages: Record<number, [string, string]> = {
  403: ['Forbidden', "You don't have permission to do that."],
  404: ['Page not found', "The page you're looking for doesn't exist or was removed."],
  419: ['Page expired', 'Your session timed out. Refresh the page and try again.'],
  429: ['Too many requests', 'Slow down a little and try again in a moment.'],
  500: ['Something went wrong', 'An unexpected error occurred. Please try again.'],
  503: ['Service unavailable', "We're down for maintenance. Please check back soon."],
}

export default function Error({ status }: { status: number }) {
  const [title, description] = messages[status] ?? ['Unexpected error', 'An unexpected error occurred. Please try again.']
  return (
    <AppLayout title={title}>
      <div className="flex flex-col items-center py-20 text-center">
        <p className="font-serif text-7xl tabular-nums text-muted-foreground/60">{status}</p>
        <h1 className="mt-4 text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-2 max-w-sm text-sm text-muted-foreground">{description}</p>
        <div className="mt-8 flex gap-2">
          <Button asChild className="rounded-lg">
            <Link href="/posts">Back to home</Link>
          </Button>
          <Button variant="ghost" className="rounded-lg" onClick={() => window.history.back()}>
            Go back
          </Button>
        </div>
      </div>
    </AppLayout>
  )
}
