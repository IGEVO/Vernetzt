import { describe, expect, it } from 'vitest'
import { htmlToText, sanitizeEmailHtml } from '@/services/html'

describe('E-Mail HTML', () => {
  it('entfernt ausführbaren und eingebetteten Inhalt', () => {
    const value = sanitizeEmailHtml('<p onclick="alert(1)">Hallo <strong>Welt</strong></p><script>alert(1)</script><img src=x onerror=alert(1)>')
    expect(value).toBe('<p>Hallo <strong>Welt</strong></p>')
  })

  it('erlaubt sichere Links und erzeugt Klartext', () => {
    const value = sanitizeEmailHtml('<p>Mehr unter <a href="https://example.org">Beispiel</a></p>')
    expect(value).toContain('href="https://example.org"')
    expect(htmlToText(value)).toBe('Mehr unter Beispiel')
  })

  it('entfernt javascript URLs', () => {
    expect(sanitizeEmailHtml('<a href="javascript:alert(1)">Nein</a>')).toBe('<a>Nein</a>')
  })

  it('entfernt andere Schemata wie data: und vbscript:', () => {
    expect(sanitizeEmailHtml('<a href="data:text/html,x">Nein</a>')).toBe('<a>Nein</a>')
    expect(sanitizeEmailHtml('<a href="vbscript:msgbox(1)">Nein</a>')).toBe('<a>Nein</a>')
  })

  it('erlaubt mailto und relative Links', () => {
    expect(sanitizeEmailHtml('<a href="mailto:info@example.org">Mail</a>')).toContain('href="mailto:info@example.org"')
    expect(sanitizeEmailHtml('<a href="termine/2026">Termine</a>')).toContain('href="termine/2026"')
  })
})
