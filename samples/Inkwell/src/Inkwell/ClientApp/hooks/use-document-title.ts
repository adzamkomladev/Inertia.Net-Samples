import { useEffect } from 'react'

/** Keeps the document title derived from the active page. */
export function useDocumentTitle(title: string | undefined, appName: string) {
  useEffect(() => {
    document.title = title ? `${title} · ${appName}` : appName
    return () => {
      document.title = appName
    }
  }, [title, appName])
}
