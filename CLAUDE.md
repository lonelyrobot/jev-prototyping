# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ColorApi is a prototype .NET 10 Minimal API. Its one endpoint, `POST /color`, sends an uploaded image to TypeSafe's Jev model and returns which of 8 crayon colors it is closest to, along with the probability for each color, as JSON. The code is in `ColorApi/`, and the unit tests are in `ColorApi.Tests/`. `web/` is a React SPA for trying the API: drop in an image and see the results. It shares only the HTTP contract with the API, so it can move to its own repo later with `git subtree split --prefix=web`.

## Commands

Run these from the repo root. `dotnet run` and `test-images.sh` need `TYPESAFE_API_KEY`, set in the environment or in a `.env` file; a real environment variable takes precedence.

```bash
dotnet build ColorApi           # no API call
dotnet test ColorApi.Tests      # unit tests; no API key or API calls
dotnet run --project ColorApi   # start the API, then upload an image (one API call):
curl -F "image=@ColorApi/test-files/red.jpg" http://localhost:5000/color
ColorApi/test-images.sh         # start the API and run all test images (one API call per image, currently 8)

npm --prefix web install          # first run only
(cd web && npx playwright install chromium)   # first run only, for test:e2e
npm --prefix web run dev          # start the UI; each image dropped makes one API call
npm --prefix web test             # component tests; no API key or API calls
npm --prefix web run test:e2e     # real UI and API with a stub classifier; no API key or credits
npm --prefix web run build        # type-check and build
npm --prefix web run lint         # lint the UI; lint:fix applies automatic fixes
```

To run a single unit test, use `dotnet test ColorApi.Tests --filter <TestName>`.

`web/` is linted with oxlint (`web/.oxlintrc.json`), and warnings fail the run. The .NET code has no linter. `web/` doesn't use ESLint with `typescript-eslint` because that package requires `typescript <6.1.0` and this project is on TypeScript 7, so check its peer range before switching.

`dotnet run` only finds `.env` if it's in the current directory or a parent, so run from `ColorApi/` if that's where it is.

Port 5000 is the API's default, and macOS's AirPlay Receiver can also listen on it. If the UI reports it can't reach the API, use another port:

```bash
dotnet run --project ColorApi --urls http://localhost:5098
COLOR_API_URL=http://localhost:5098 npm --prefix web run dev
```

## How it works

- **`Program.cs`:** startup and the endpoint. It runs `ImagePreparer`, calls the classifier, sorts the probabilities and maps errors to HTTP statuses.
- **`ImagePreparer.cs`:** treats uploads as untrusted. It checks the format and pixel count before decoding, auto-orients the image (Jev ignores EXIF), scales it to 280–1,568 px per side, and re-encodes it as PNG. It throws `BadImageException` if the aspect ratio is too extreme to fit both limits.
- **`JevClassifier.cs`:** implements `IColorClassifier`. It attaches the image to `RequestOptions`, asks a single Choice question listing the crayon colors, and sends it to the pinned model (`Model` constant). It converts `TypeSafeApiException` to `ClassifierException`.

- **`StubClassifier.cs`:** returns a fixed result. `Program.cs` registers it instead of `JevClassifier` only when `ASPNETCORE_ENVIRONMENT=E2E`.
- **`web/`:** `App.tsx` holds the state, `api.ts` the request and error messages, and `DropZone.tsx` and `Results.tsx` the UI. In development, Vite proxies `/color` to the API (`COLOR_API_URL`), so the API needs no CORS.

HTTP statuses: 200 ok, 400 bad image, 413 upload over 20 MiB, 502 TypeSafe API error (details are only logged). A missing API key stops the app at startup. `test-images.sh` exit codes: 0 all passed, 1 at least one wrong pick or error, 2 build or server startup failed.

## Testing

- **Unit tests:** these replace `IColorClassifier` with `FakeClassifier` through `WebApplicationFactory`, so they never call TypeSafe. They generate images in memory and don't use `test-files/`.
- **`test-images.sh`:** the only check of real Jev behavior, so results depend on the model. It needs `curl` and `jq`, and uses port 5099 unless `COLOR_TEST_PORT` is set.
- **Component tests (`web/src/App.test.tsx`):** these mock `fetch`, so they can't catch a contract mismatch with the API.
- **Playwright (`web/e2e/`):** real UI, Vite proxy and API, with `StubClassifier` and a dummy key. It uses ports 5097 and 5174 and never reuses a running server, so it fails if either port is in use. Drops are simulated, because Playwright can't drag from the operating system.
- **Upload size limit:** this is a Kestrel setting that `WebApplicationFactory`'s test server doesn't apply, so unit tests can't cover it.

## Changes that span files

- **Response format:** `test-images.sh` reads `.closest` and `.confidence` from the JSON, and the endpoint tests check the same fields. `web/src/api.ts` and the Playwright tests also depend on them. Renaming a `ColorResult` property can break all of these. Changing the route also means updating `web/vite.config.ts`.
- **Color options:** the script compares each image's filename to the pick, so `test-files/<color>.jpg` must match the option key in the `ChoiceQuestion` dictionary. Only `.jpg` files are picked up. `web/src/colors.ts` has the label and swatch for each key, and unknown keys fall back to the raw key.
- **Upload limits:** the accepted formats and the 20 MiB limit are duplicated in `web/src/api.ts`. The server is the authority.
- **502 messages:** `web/src/api.ts` treats a 502 with a problem `detail` as a model error, and any other 502 as an unreachable API, because Vite's proxy returns 502 when the API is down. Keep the `detail` in the API's 502 response.
- **Test images:** these aren't covered by the repo's MIT license. Credit any added image in `ColorApi/test-files/ATTRIBUTION.md`.

## SDK caveats

- `tryAGI.TypeSafeAI` is a community SDK. SDK docs: https://tryagi.github.io/TypeSafeAI/
- The Web SDK imports `System.Net.Http.Json`, which also defines `JsonContent`, so `JevClassifier.cs` needs its alias for the TypeSafe type.
- Image input is experimental on TypeSafe's side ("danger zone"). The SDK sends the required opt-in header.
