import { xsrf } from './xsrf'
import type { PresignResponse, UploadedAsset } from '@/types'

const MB = 1024 * 1024
const limits: Record<string, number> = {
  'image/png': 5 * MB,
  'image/jpeg': 5 * MB,
  'image/webp': 5 * MB,
  'image/gif': 5 * MB,
  'application/pdf': 20 * MB,
}

async function postJson<T>(url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'X-XSRF-TOKEN': xsrf() },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (res.ok) return res.json()
  let msg = `Upload failed (${res.status}).`
  try {
    const data = await res.json()
    const first = data?.errors ? Object.values<string[]>(data.errors).flat()[0] : null
    msg = first ?? data?.title ?? msg
  } catch { /* non-JSON body */ }
  throw new Error(msg)
}

function put(url: string, file: File, headers: Record<string, string>, onProgress: (pct: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', url)
    for (const [k, v] of Object.entries(headers)) xhr.setRequestHeader(k, v)
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgress(Math.round((e.loaded / e.total) * 100)) }
    xhr.onload = () => (xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(`Storage rejected the upload (${xhr.status}).`)))
    xhr.onerror = () => reject(new Error('Network error while uploading.'))
    xhr.send(file)
  })
}

export function useUpload() {
  let progress = $state(0)
  let uploading = $state(false)
  let error = $state<string | null>(null)

  async function upload(file: File): Promise<UploadedAsset> {
    error = null
    const max = limits[file.type]
    try {
      if (!max) throw new Error('Unsupported file type.')
      if (file.size > max) throw new Error(`File is too large (max ${max / MB} MB).`)
      uploading = true
      progress = 0
      const presign = await postJson<PresignResponse>('/uploads/presign', { contentType: file.type, size: file.size, fileName: file.name })
      await put(presign.url, file, presign.headers, (p) => (progress = p))
      return await postJson<UploadedAsset>(`/uploads/${presign.assetId}/confirm`)
    } catch (e) {
      error = e instanceof Error ? e.message : 'Upload failed.'
      throw e
    } finally {
      uploading = false
    }
  }

  return {
    get progress() { return progress },
    get uploading() { return uploading },
    get error() { return error },
    clearError: () => { error = null },
    upload,
  }
}
