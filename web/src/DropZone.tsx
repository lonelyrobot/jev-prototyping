import { useEffect, useState, type ChangeEvent, type DragEvent } from 'react'
import { ACCEPTED_TYPES } from './api'

interface DropZoneProps {
  onFiles: (files: File[]) => void
}

export default function DropZone({ onFiles }: DropZoneProps) {
  const [dragging, setDragging] = useState(false)

  // A drop that misses the zone would otherwise make the browser navigate to the file.
  useEffect(() => {
    const prevent = (event: Event) => event.preventDefault()
    window.addEventListener('dragover', prevent)
    window.addEventListener('drop', prevent)
    return () => {
      window.removeEventListener('dragover', prevent)
      window.removeEventListener('drop', prevent)
    }
  }, [])

  const handleDragOver = (event: DragEvent) => {
    event.preventDefault()
    setDragging(true)
  }

  const handleDragLeave = (event: DragEvent) => {
    // Moving onto a child element also fires dragleave, so ignore it.
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false)
  }

  const handleDrop = (event: DragEvent) => {
    event.preventDefault()
    setDragging(false)
    onFiles(Array.from(event.dataTransfer.files))
  }

  const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
    onFiles(Array.from(event.target.files ?? []))
    event.target.value = '' // lets the same file be chosen again
  }

  return (
    <label
      className={dragging ? 'drop-zone dragging' : 'drop-zone'}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      <input
        className="visually-hidden"
        type="file"
        accept={ACCEPTED_TYPES.join(',')}
        onChange={handleChange}
      />
      <strong>Drop an image here</strong>
      <span>or click to browse. PNG, JPEG, WebP, GIF or BMP, up to 20 MiB.</span>
    </label>
  )
}
