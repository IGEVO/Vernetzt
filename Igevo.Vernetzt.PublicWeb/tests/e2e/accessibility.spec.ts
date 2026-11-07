import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

test('start page is server rendered and has no serious accessibility violations', async ({ page }) => {
  const response = await page.goto('/', { waitUntil: 'networkidle' })
  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()

  const results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter(item => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])
})

test('main navigation is keyboard reachable', async ({ page }) => {
  await page.goto('/')
  await page.keyboard.press('Tab')
  await expect(page.getByText('Zum Inhalt springen')).toBeFocused()
})

for (const colorScheme of ['light', 'dark'] as const) {
  for (const path of ['/vereine', '/veranstaltungen', '/datenschutz']) {
    test(`${path} has no serious accessibility violations (${colorScheme})`, async ({ page }) => {
      await page.emulateMedia({ colorScheme })
      const response = await page.goto(path, { waitUntil: 'networkidle' })
      expect(response?.status()).toBe(200)
      const results = await new AxeBuilder({ page }).analyze()
      expect(results.violations.filter(item => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])
    })
  }
}

test('public API proxy rejects paths outside the allowlist', async ({ request }) => {
  const traversal = await request.get('/api/public/v1/%2e%2e/%2e%2e/users/me')
  expect(traversal.status()).toBe(404)
  const write = await request.post('/api/public/v1/clubs')
  expect(write.status()).toBe(405)
})

test.describe('departments and groups', () => {
  /** Finds a department with groups in the connected backend (the development seed has TSV › Handball). */
  async function findDepartmentWithGroups(request: import('@playwright/test').APIRequestContext) {
    const clubs = await (await request.get('/api/public/v1/clubs?pageSize=100')).json()
    for (const summary of clubs.items ?? []) {
      const club = await (await request.get(`/api/public/v1/clubs/${summary.id}`)).json()
      const department = (club.departments ?? []).find((item: { groupCount: number }) => item.groupCount > 0)
      if (department) {
        const details = await (await request.get(`/api/public/v1/departments/${department.id}`)).json()
        return { club, department: details }
      }
    }
    return null
  }

  for (const colorScheme of ['light', 'dark'] as const) {
    test(`group page shows the hierarchy and has no serious accessibility violations (${colorScheme})`, async ({ page, request }) => {
      const found = await findDepartmentWithGroups(request)
      test.skip(!found, 'Backend has no department with groups')
      const { club, department } = found!
      const group = department.groups[0]

      await page.emulateMedia({ colorScheme })
      await page.goto(`/abteilungen/${department.id}/x`, { waitUntil: 'networkidle' })
      await expect(page.getByTestId('organization-hierarchy')).toHaveText(`Abteilung von ${club.name}`)
      await page.getByRole('region', { name: 'Gruppen' }).getByRole('link', { name: new RegExp(group.name) }).click()

      await expect(page.getByRole('heading', { level: 1, name: group.name })).toBeVisible()
      await expect(page.getByTestId('organization-hierarchy')).toHaveText(`Gruppe der Abteilung ${department.name} · ${club.name}`)
      const breadcrumbs = page.getByRole('navigation', { name: 'Brotkrumen' })
      await expect(breadcrumbs.getByRole('link', { name: department.name })).toBeVisible()
      await expect(breadcrumbs.getByRole('link', { name: club.name })).toBeVisible()

      const results = await new AxeBuilder({ page }).analyze()
      expect(results.violations.filter(item => ['critical', 'serious'].includes(item.impact ?? ''))).toEqual([])
    })
  }
})
