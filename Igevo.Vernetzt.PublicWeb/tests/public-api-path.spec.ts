import { describe, expect, it } from 'vitest'
import { toAllowedPublicApiPath } from '../server/utils/public-api-path'

const id = '0f8fad5b-d9cb-469f-a165-70867728950e'

describe('public API proxy allowlist', () => {
  it.each([
    '/api/public/v1/clubs',
    '/api/public/v1/clubs?query=tsv&page=2',
    '/api/public/v1/club-types',
    `/api/public/v1/clubs/${id}`,
    `/api/public/v1/departments/${id}/logo?size=medium`,
    `/api/public/v1/clubs/${id}/contact-details`,
    `/api/public/v1/departments/${id}/contact-details`,
    `/api/public/v1/contacts/${id}/contact-details`,
    `/api/public/v1/events/${id}/media/${id}/preview/960`,
    `/api/public/v1/events/${id}/media/${id}/file`,
    '/api/public/v1/portal/logo?v=638600000000000000'
  ])('forwards %s', (path) => {
    expect(toAllowedPublicApiPath(path)).toBe(path)
  })

  it.each([
    '/api/public/v1/%2e%2e/%2e%2e/users/me',
    '/api/public/v1/../../users/me',
    '/api/public/v1/clubs/..%2f..%2fusers',
    '/api/public/v1/clubs/not-a-guid',
    `/api/public/v1/contacts/${id}`,
    `/api/public/v1/events/${id}/contact-details`,
    '/api/public/v1/openapi.json',
    '/api/users/me',
    '/api/public/v1/portal/logo/../../../users/me',
    `/api/public/v1/events/${id}/media/${id}/preview/1/x`,
    `/api/public/v1/clubs?${'x'.repeat(2000)}`
  ])('rejects %s', (path) => {
    expect(toAllowedPublicApiPath(path)).toBeNull()
  })
})
