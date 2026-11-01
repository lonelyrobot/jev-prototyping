// Keys match the options in ColorApi/JevClassifier.cs.
const COLORS: Record<string, { label: string; swatch: string }> = {
  red: { label: 'Red', swatch: '#ee204d' },
  orange: { label: 'Orange', swatch: '#ff7538' },
  yellow: { label: 'Yellow', swatch: '#fce883' },
  green: { label: 'Green', swatch: '#1cac78' },
  blue: { label: 'Blue', swatch: '#1f75fe' },
  violet: { label: 'Violet', swatch: '#926eae' },
  brown: { label: 'Brown', swatch: '#b4674d' },
  black: { label: 'Black', swatch: '#232323' },
}

const NEUTRAL_SWATCH = '#9ca3af'

// An unknown key falls back to the raw key, so adding a color to the API doesn't break the UI.
export function colorInfo(key: string) {
  return Object.hasOwn(COLORS, key) ? COLORS[key] : { label: key, swatch: NEUTRAL_SWATCH }
}
