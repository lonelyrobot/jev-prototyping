# ColorApi

A prototype API that sends an image to TypeSafe's Jev model and asks which of the
8 classic crayon colors (red, orange, yellow, green, blue, violet, brown, black)
it's closest to. Returns the pick plus the probability for each color as JSON.
`web/` is a small React UI where you can drop in an image and see the results.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A TypeSafe API key
- `curl` and `jq`, for `test-images.sh`
- [Node.js](https://nodejs.org/), for the web UI

## Setup

`dotnet run` restores the NuGet packages automatically.

```bash
cd ColorApi
echo 'TYPESAFE_API_KEY=...' > .env       # or export it; a real environment variable wins
dotnet run
```

Then, from `ColorApi/` in another terminal, use the port shown in the `dotnet run` output:

```bash
curl -F "image=@test-files/red.jpg" http://localhost:5000/color
```

## Web UI

With the API running, from the repo root in another terminal:

```bash
npm --prefix web install
npm --prefix web run dev
```

Open the URL it prints. Each image you drop makes one TypeSafe API call. Vite proxies `/color` to
`http://localhost:5000`. That port can also be in use by macOS's AirPlay Receiver, so if the UI
can't reach the API, use another one:

```bash
dotnet run --project ColorApi --urls http://localhost:5098
COLOR_API_URL=http://localhost:5098 npm --prefix web run dev
```

## API

`POST /color` takes `multipart/form-data` with the file in a field named `image`.

Example response, with illustrative numbers:

```json
{
  "closest": "red",
  "confidence": 0.94,
  "probabilities": [
    { "color": "red", "probability": 0.94 },
    { "color": "orange", "probability": 0.03 }
  ]
}
```

| Status | Meaning |
|---|---|
| 200 | Color picked |
| 400 | Invalid or unsupported image, too many pixels, or missing `image` field |
| 413 | Upload over 20 MiB |
| 502 | TypeSafe API error (details are only logged) |

## Testing

```bash
dotnet test ColorApi.Tests
```

Runs the unit tests, which need no API key and make no API calls.

```bash
npm --prefix web test
```

Runs the UI's component tests, with `fetch` mocked.

```bash
npm --prefix web run lint
```

Lints the UI's code with oxlint.

```bash
(cd web && npx playwright install chromium)   # first run only
npm --prefix web run test:e2e
```

Runs the real UI against the real API with only the Jev call stubbed, so it needs no API key and
spends no credits.

```bash
ColorApi/test-images.sh
```

Starts the API, uploads every image in `ColorApi/test-files/` and checks that Jev picks the color
in the filename. This makes one TypeSafe API call per image, currently 8 in total.

Exit codes: 0 all passed, 1 at least one wrong pick or error, 2 build or server startup failed.

## Notes

- Accepts PNG, JPEG, WebP, GIF and BMP, up to 50 million pixels.
- Image is auto-oriented (Jev ignores EXIF), scaled into 280–1,568 px per side, re-encoded as PNG.
- Image input is experimental on TypeSafe's side ("danger zone"); the SDK sends the opt-in header.
- The API has no authentication or rate limiting, and every request spends TypeSafe credits. Add both before exposing it publicly.

## Ideas

- `--local` mode: nearest crayon color in Lab space as a free baseline to compare against Jev.

## License

The code is available under the [MIT License](LICENSE). This doesn't cover the images in
`ColorApi/test-files/`, which keep their own licenses; see [ATTRIBUTION.md](ColorApi/test-files/ATTRIBUTION.md).
