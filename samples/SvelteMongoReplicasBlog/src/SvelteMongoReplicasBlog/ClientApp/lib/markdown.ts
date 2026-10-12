import MarkdownIt from 'markdown-it'

const md = new MarkdownIt({ html: false, linkify: true, breaks: true })

const defaultLink = md.renderer.rules.link_open ?? ((tokens, idx, options, _env, self) => self.renderToken(tokens, idx, options))
md.renderer.rules.link_open = (tokens, idx, options, env, self) => {
  tokens[idx]!.attrSet('target', '_blank')
  tokens[idx]!.attrSet('rel', 'nofollow noopener noreferrer')
  return defaultLink(tokens, idx, options, env, self)
}

export const renderMarkdown = (src: string): string => md.render(src)
