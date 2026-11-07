import { describe, expect, it } from 'vitest'
import { applyTemplate, portalTemplates, withoutUnchangedTemplate } from '@/services/portalTemplates'
import type { PortalLegalPageDto } from '@/types/api'

const page = (html: string): PortalLegalPageDto => ({ mode: 'text', html, url: '' })

describe('portal legal page templates', () => {
  it('fills only empty pages', () => {
    const empty = page('  ')
    expect(applyTemplate('imprint', empty)).toBe(true)
    expect(empty.html).toBe(portalTemplates.imprint)

    const filled = page('<p>Eigener Text</p>')
    expect(applyTemplate('imprint', filled)).toBe(false)
    expect(filled.html).toBe('<p>Eigener Text</p>')
  })

  it('never publishes an unchanged template', () => {
    expect(withoutUnchangedTemplate('privacy', page(portalTemplates.privacy)).html).toBe('')
    expect(withoutUnchangedTemplate('privacy', page('<p>Angepasst</p>')).html).toBe('<p>Angepasst</p>')
  })
})
