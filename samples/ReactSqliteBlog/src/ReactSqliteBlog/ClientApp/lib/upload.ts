export interface UploadResult {
  id: string
  url: string
}

export function xsrfToken() {
  const m = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/)
  return m ? decodeURIComponent(m[1]!) : ''
}

/**
 * Uploads an image to POST /uploads (multipart field "file"). Purpose "avatar" or "post".
 * Raw fetch, so the antiforgery header is set by hand (Inertia's router does it automatically).
 */
export async function uploadImage(file: File, purpose: 'post' | 'avatar' = 'post'): Promise<UploadResult> {
  const body = new FormData()
  body.append('file', file)
  body.append('purpose', purpose)
  const res = await fetch('/uploads', {
    method: 'POST',
    body,
    headers: { 'X-XSRF-TOKEN': xsrfToken(), Accept: 'application/json' },
    credentials: 'same-origin',
  })
  if (!res.ok) {
    const data = (await res.json().catch(() => null)) as { error?: string } | null
    throw new Error(data?.error ?? `Upload failed (${res.status})`)
  }
  return (await res.json()) as UploadResult
}
