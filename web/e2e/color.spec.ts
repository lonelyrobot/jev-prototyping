import { expect, test, type Page } from '@playwright/test'

// Playwright can't drag from the operating system, so this generates an image on a canvas in the
// page and dispatches the drag events on the zone with it.
async function dropGeneratedImage(page: Page, width: number, height: number) {
  const dataTransfer = await page.evaluateHandle(
    async (size) => {
      const canvas = document.createElement('canvas')
      canvas.width = size.width
      canvas.height = size.height
      const context = canvas.getContext('2d')!
      context.fillStyle = '#1cac78'
      context.fillRect(0, 0, size.width, size.height)
      const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/png'))

      const transfer = new DataTransfer()
      transfer.items.add(new File([blob!], 'generated.png', { type: 'image/png' }))
      return transfer
    },
    { width, height },
  )

  const zone = page.locator('.drop-zone')
  await zone.dispatchEvent('dragenter', { dataTransfer })
  await zone.dispatchEvent('dragover', { dataTransfer })
  await zone.dispatchEvent('drop', { dataTransfer })
}

test.beforeEach(async ({ page }) => {
  await page.goto('/')
})

test('a valid image shows the stub result from the API', async ({ page }) => {
  await dropGeneratedImage(page, 400, 400)

  const results = page.getByRole('region', { name: 'Results' })
  await expect(results.getByRole('heading', { name: 'Green' })).toBeVisible()
  await expect(results.getByText('75% confidence')).toBeVisible()
  await expect(results.getByRole('listitem')).toHaveCount(8)
  // StubClassifier returns these unsorted, so this also checks the API's sorting.
  await expect(results.getByRole('listitem').first()).toContainText('Green')
  await expect(page.getByRole('img', { name: 'Your upload' })).toBeVisible()
})

test('an image the API rejects shows its aspect-ratio message', async ({ page }) => {
  await dropGeneratedImage(page, 2000, 100)

  await expect(page.getByRole('alert')).toContainText('Aspect ratio too extreme')
  await expect(page.getByRole('region', { name: 'Results' })).toHaveCount(0)
})
