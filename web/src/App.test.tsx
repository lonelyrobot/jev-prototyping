import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { MAX_UPLOAD_BYTES, type ColorResult } from './api'

const png = (name = 'a.png') => new File(['x'], name, { type: 'image/png' })

const result = (closest: string, confidence = 0.9): ColorResult => ({
  closest,
  confidence,
  probabilities: [
    { color: closest, probability: confidence },
    { color: 'orange', probability: 1 - confidence },
  ],
})

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const fetchMock = vi.fn<typeof fetch>()

function drop(...files: File[]) {
  fireEvent.drop(screen.getByLabelText(/drop an image/i), { dataTransfer: { files } })
}

beforeEach(() => {
  fetchMock.mockReset()
  vi.stubGlobal('fetch', fetchMock)
  // jsdom doesn't implement these.
  let previews = 0
  URL.createObjectURL = vi.fn(() => `blob:preview-${++previews}`)
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('uploading', () => {
  it('sends the file in the image field and shows the closest color and all bars', async () => {
    fetchMock.mockResolvedValue(json(result('red', 0.9)))
    render(<App />)
    const file = png()

    drop(file)

    const heading = await screen.findByRole('heading', { name: 'Red' })
    expect(heading).toBeTruthy()
    expect(screen.getByText('90% confidence')).toBeTruthy()
    const bars = within(screen.getByRole('region', { name: 'Results' })).getAllByRole('listitem')
    expect(bars.map((bar) => bar.querySelector('.bar-label')?.textContent)).toEqual(['Red', 'Orange'])

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/color')
    expect(init?.method).toBe('POST')
    expect((init?.body as FormData).get('image')).toEqual(file)
  })

  it('works through the file input as well', async () => {
    fetchMock.mockResolvedValue(json(result('blue')))
    render(<App />)

    fireEvent.change(screen.getByLabelText(/drop an image/i), { target: { files: [png()] } })

    expect(await screen.findByRole('heading', { name: 'Blue' })).toBeTruthy()
  })

  it('uploads only the first of several files and says so', async () => {
    fetchMock.mockResolvedValue(json(result('red')))
    render(<App />)
    const first = png('first.png')

    drop(first, png('second.png'))

    await screen.findByRole('heading', { name: 'Red' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect((fetchMock.mock.calls[0][1]?.body as FormData).get('image')).toEqual(first)
    expect(screen.getByText('Only the first of 2 files was used.')).toBeTruthy()
  })

  it('renders an unknown color with its raw key', async () => {
    fetchMock.mockResolvedValue(json(result('teal')))
    render(<App />)

    drop(png())

    expect(await screen.findByRole('heading', { name: 'teal' })).toBeTruthy()
  })

  it('aborts the first request when a second image is dropped', async () => {
    let firstSignal: AbortSignal | undefined
    fetchMock.mockImplementationOnce(
      (_url, init) =>
        new Promise((_resolve, reject) => {
          firstSignal = init?.signal ?? undefined
          firstSignal?.addEventListener('abort', () =>
            reject(new DOMException('Aborted', 'AbortError')),
          )
        }),
    )
    fetchMock.mockResolvedValueOnce(json(result('blue')))
    render(<App />)

    drop(png('slow.png'))
    drop(png('fast.png'))

    expect(await screen.findByRole('heading', { name: 'Blue' })).toBeTruthy()
    expect(firstSignal?.aborted).toBe(true)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('revokes the previous preview when the image is replaced', async () => {
    fetchMock.mockImplementation(async () => json(result('red')))
    render(<App />)

    drop(png())
    await screen.findByRole('heading', { name: 'Red' })
    expect(URL.revokeObjectURL).not.toHaveBeenCalled()

    drop(png())
    await waitFor(() => expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1'))
  })
})

describe('checks before uploading', () => {
  it('rejects an unsupported type without calling the API', async () => {
    render(<App />)

    drop(new File(['<svg/>'], 'a.svg', { type: 'image/svg+xml' }))

    expect((await screen.findByRole('alert')).textContent).toMatch(/PNG, JPEG, WebP, GIF or BMP/)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('rejects a file over 20 MiB without calling the API', async () => {
    render(<App />)
    const big = png()
    Object.defineProperty(big, 'size', { value: MAX_UPLOAD_BYTES + 1 })

    drop(big)

    expect((await screen.findByRole('alert')).textContent).toBe('Image is over the 20 MiB limit.')
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

describe('API errors', () => {
  const unreachable = "Couldn't reach the API. Is it running?"

  it.each<[string, () => Response, string]>([
    ['400 with detail', () => json({ detail: 'Aspect ratio too extreme.' }, 400), 'Aspect ratio too extreme.'],
    ['400 without detail', () => json({}, 400), "That image couldn't be read."],
    ['400 with a non-JSON body', () => new Response('nope', { status: 400 }), "That image couldn't be read."],
    ['413', () => new Response('', { status: 413 }), 'Image is over the 20 MiB limit.'],
    ['502', () => json({ detail: 'Upstream model error.' }, 502), 'The color model is unavailable. Try again.'],
    ['a 502 from a proxy with the API down', () => new Response('', { status: 502 }), unreachable],
    ['another status', () => new Response('', { status: 500 }), unreachable],
    ['a non-JSON 200', () => new Response('<html>', { status: 200 }), unreachable],
  ])('shows the mapped message for %s', async (_name, response, message) => {
    fetchMock.mockResolvedValue(response())
    render(<App />)

    drop(png())

    expect((await screen.findByRole('alert')).textContent).toBe(message)
  })

  it('shows the mapped message for a network failure', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))
    render(<App />)

    drop(png())

    expect((await screen.findByRole('alert')).textContent).toBe(unreachable)
  })
})
