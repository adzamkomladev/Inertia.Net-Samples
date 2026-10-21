import { useState } from 'react'

/**
 * Local state that re-syncs when the server-provided value changes, without an effect.
 * Uses the documented "adjusting state during render" pattern so a fresh Inertia prop
 * resets optimistic state while local edits survive re-renders.
 */
export function useOptimisticValue<T>(value: T): [T, (next: T | ((previous: T) => T)) => void] {
  const [state, setState] = useState<T>(value)
  const [seen, setSeen] = useState<T>(value)
  if (!Object.is(seen, value)) {
    setSeen(value)
    setState(value)
  }
  return [state, setState]
}
