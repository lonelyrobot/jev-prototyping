import type { ColorResult } from './api'
import { colorInfo } from './colors'

const percent = (value: number) => `${Math.round(value * 100)}%`

export default function Results({ result }: { result: ColorResult }) {
  const closest = colorInfo(result.closest)

  return (
    <section className="results" aria-label="Results">
      <div className="closest">
        <span className="swatch" style={{ background: closest.swatch }} />
        <div>
          <h2>{closest.label}</h2>
          <p>{percent(result.confidence)} confidence</p>
        </div>
      </div>

      {/* The API sorts these highest first. */}
      <ul className="bars">
        {result.probabilities.map(({ color, probability }) => {
          const info = colorInfo(color)
          return (
            <li key={color}>
              <span className="bar-label">{info.label}</span>
              <span className="bar-track">
                <span
                  className="bar-fill"
                  style={{ width: percent(probability), background: info.swatch }}
                />
              </span>
              <span className="bar-value">{percent(probability)}</span>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
