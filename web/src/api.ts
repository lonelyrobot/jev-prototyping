export interface ColorProbability {
  color: string
  probability: number
}

export interface ColorResult {
  closest: string
  confidence: number
  probabilities: ColorProbability[]
}

// These duplicate the server's limits to avoid wasted requests. The server stays the authority:
// the formats are in ColorApi/ImagePreparer.cs and the size limit is in ColorApi/Program.cs.
export const ACCEPTED_TYPES = ['image/png', 'image/jpeg', 'image/webp', 'image/gif', 'image/bmp']
export const MAX_UPLOAD_BYTES = 20 * 1024 * 1024

const UNREACHABLE = "Couldn't reach the API. Is it running?"

export class ClassifyError extends Error {}

// Returns a message if the file shouldn't be uploaded.
export function validateFile(file: File): string | null {
  if (!ACCEPTED_TYPES.includes(file.type)) return 'Use a PNG, JPEG, WebP, GIF or BMP image.'
  if (file.size > MAX_UPLOAD_BYTES) return 'Image is over the 20 MiB limit.'
  return null
}

export async function classify(file: File, signal: AbortSignal): Promise<ColorResult> {
  const form = new FormData()
  form.append('image', file)

  let response: Response
  try {
    response = await fetch('/color', { method: 'POST', body: form, signal })
  } catch (error) {
    if (signal.aborted) throw error
    throw new ClassifyError(UNREACHABLE)
  }

  if (!response.ok) throw new ClassifyError(await errorMessage(response))

  try {
    return (await response.json()) as ColorResult
  } catch (error) {
    if (signal.aborted) throw error
    throw new ClassifyError(UNREACHABLE)
  }
}

async function errorMessage(response: Response): Promise<string> {
  switch (response.status) {
    case 400:
      return (await readDetail(response)) ?? "That image couldn't be read."
    case 413:
      return 'Image is over the 20 MiB limit.'
    case 502:
      // The API's own 502 has a problem body. A proxy also returns 502 when the API is down.
      return (await readDetail(response)) ? 'The color model is unavailable. Try again.' : UNREACHABLE
    default:
      return UNREACHABLE
  }
}

// A failure from Kestrel or the proxy may not be JSON.
async function readDetail(response: Response): Promise<string | null> {
  try {
    const body: unknown = await response.json()
    const detail = (body as { detail?: unknown } | null)?.detail
    return typeof detail === 'string' && detail ? detail : null
  } catch {
    return null
  }
}
