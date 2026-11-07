import DOMPurify from 'dompurify'
export const allowedEmailTags = ['p', 'br', 'strong', 'b', 'em', 'i', 'u', 's', 'h1', 'h2', 'h3', 'ul', 'ol', 'li', 'blockquote', 'a']
export function sanitizeEmailHtml(html: string): string {
  return DOMPurify.sanitize(html, { ALLOWED_TAGS: allowedEmailTags, ALLOWED_ATTR: ['href', 'title'], ALLOWED_URI_REGEXP: /^(?:(?:https?|mailto):|[^a-z]|[a-z+.-]+(?:[^a-z+.-:]|$))/i })
}
export function htmlToText(html: string): string { const element = document.createElement('div'); element.innerHTML = sanitizeEmailHtml(html); return element.textContent?.trim() ?? '' }
