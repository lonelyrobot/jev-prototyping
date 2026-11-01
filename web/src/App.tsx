import { useEffect, useRef, useState } from 'react'
import { classify, ClassifyError, validateFile, type ColorResult } from './api'
import DropZone from './DropZone'
import Results from './Results'
import './App.css'

type State =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; result: ColorResult }
  | { status: 'error'; message: string }

export default function App() {
  const [state, setState] = useState<State>({ status: 'idle' })
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const inFlight = useRef<AbortController | null>(null)

  useEffect(() => {
    if (!previewUrl) return
    return () => URL.revokeObjectURL(previewUrl)
  }, [previewUrl])

  useEffect(() => () => inFlight.current?.abort(), [])

  const handleFiles = async (files: File[]) => {
    const file = files[0]
    if (!file) return

    // A slow earlier response must not overwrite this one.
    inFlight.current?.abort()
    inFlight.current = null

    setNotice(files.length > 1 ? `Only the first of ${files.length} files was used.` : null)

    const invalid = validateFile(file)
    if (invalid) {
      setPreviewUrl(null)
      setState({ status: 'error', message: invalid })
      return
    }

    const controller = new AbortController()
    inFlight.current = controller
    setPreviewUrl(URL.createObjectURL(file))
    setState({ status: 'loading' })

    try {
      const result = await classify(file, controller.signal)
      if (!controller.signal.aborted) setState({ status: 'success', result })
    } catch (error) {
      if (controller.signal.aborted) return
      const message = error instanceof ClassifyError ? error.message : 'Something went wrong.'
      setState({ status: 'error', message })
    }
  }

  return (
    <main>
      <h1>Crayon Color Match</h1>
      <p className="intro">Find out which of 8 classic crayon colors an image is closest to.</p>

      <DropZone onFiles={handleFiles} />

      {notice && <p className="notice">{notice}</p>}
      {state.status === 'error' && (
        <p className="error" role="alert">
          {state.message}
        </p>
      )}

      {(previewUrl || state.status === 'success') && (
        <div className="output">
          {previewUrl && <img className="preview" src={previewUrl} alt="Your image" />}
          {state.status === 'loading' && (
            <p className="loading" role="status">
              Checking the color…
            </p>
          )}
          {state.status === 'success' && <Results result={state.result} />}
        </div>
      )}
    </main>
  )
}
