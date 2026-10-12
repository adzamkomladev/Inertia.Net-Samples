export function xsrf(): string {
  const m = document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]*)/)
  return m ? decodeURIComponent(m[1]) : ''
}
