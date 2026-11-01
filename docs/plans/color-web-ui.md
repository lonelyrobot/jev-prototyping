# Rename to ColorApi and add a web UI

**Status:** Implemented. Where this plan and the source differ, the source is correct. Differences:

- A 502 with no problem `detail` shows "Couldn't reach the API", because Vite's proxy returns 502 when the API is down. A 502 with a `detail` shows the model-unavailable message.
- The Playwright ignore entries are in `web/.gitignore`, which the template also uses for `node_modules` and `dist`, so they move with the folder.

## Context

The project is still named `CrayonCli`, although it's no longer a CLI and "color" describes it better. `POST /crayon` can also only be tried with `curl` or `test-images.sh`. This renames the project first, then adds a small React SPA where you drag in an image, see a preview, and see the closest color and the probability for each color.

The rest of this plan uses the new names.

## Part 1: Rename to ColorApi

Do this as its own commit, before any UI work, so the rename stays easy to review.

| Current | New |
|---|---|
| `CrayonCli/`, `CrayonCli.csproj` | `ColorApi/`, `ColorApi.csproj` |
| `CrayonCli.Tests/`, `CrayonCli.Tests.csproj` | `ColorApi.Tests/`, `ColorApi.Tests.csproj` |
| `POST /crayon` | `POST /color` |
| `CrayonResult`, `ICrayonClassifier` | `ColorResult`, `IColorClassifier` |
| `CrayonApiFactory`, `CrayonEndpointTests` | `ColorApiFactory`, `ColorEndpointTests` |
| `CRAYON_TEST_PORT` | `COLOR_TEST_PORT` |

- **Moves:** use `git mv`, so history follows the files. `CrayonCli/.env` is untracked, so move it by hand, or the API won't find its key. Delete the old `bin/` and `obj/` folders.
- **Also update:** the project reference in the test `.csproj`, `Program.cs` comments, `test-images.sh`, `README.md` and `CLAUDE.md`.
- **Keep the Jev question unchanged:** the task text ("Pick the crayon color..."), the `closest_crayon` question name and the 8 option keys. Rewording them could change the model's picks, and the app still chooses from crayon colors. Rename only the local variable `crayon`, to `closestColor`.
- **Keep the JSON fields:** `closest`, `confidence` and `probabilities` don't mention crayons.
- **Keep the history:** `docs/plans/crayon-api-conversion.md` is a record of past work, so it isn't renamed or rewritten.
- **Breaking change:** the route change breaks existing `curl` commands. Nothing else depends on the route yet.

**Verify the rename:**
- `dotnet build ColorApi` and `dotnet test ColorApi.Tests` pass.
- `git grep -i crayon` only finds the kept Jev question, descriptions of the 8 crayon colors, and the old plan doc.
- `ColorApi/test-images.sh` passes all 8 images. This costs 8 API calls, and confirms the unchanged question still gives the same picks.

## Part 2: Web UI

### Decisions

- **Repo: start here, in a top-level `web/` folder, and move it when needed.** One person is changing the API and UI together, and there's no deployment yet. A second repo would only add overhead.
- **Moving later is cheap.** The UI and API share only the HTTP contract, with no shared code or build. `git subtree split --prefix=web` extracts the folder with its history.
- **When to move:** the UI gets a different owner or release schedule, or a second client makes the API a product in its own right.
- **Not inside `ColorApi/`:** the Web SDK project would pick up the frontend's files through its default globs.
- **Stack:** Vite, React and TypeScript. Plain CSS, native drag-and-drop events, and `fetch`. No router, state library, UI kit or `react-dropzone`, because this is one screen with one request.
- **No CORS:** in development, Vite proxies `/color` to the API, so the browser sees one origin and the API doesn't need CORS.
- **One image at a time:** if several files are dropped, the UI uses the first and says so. Each drop costs one API call, and batch uploads are already out of scope for the API.

## Layout

```
web/
  package.json
  vite.config.ts      # dev proxy and Vitest config
  index.html
  src/
    main.tsx
    App.tsx           # state machine and page layout
    api.ts            # classify(file, signal), response types, error mapping
    DropZone.tsx      # drag and drop, plus click to browse
    Results.tsx       # closest color and probability bars
    colors.ts         # color key to swatch color and label
    App.css
    App.test.tsx
  playwright.config.ts
  e2e/
    color.spec.ts     # real UI and real API, with a stub classifier
ColorApi/
  StubClassifier.cs   # fixed result, used only in the E2E environment
```

## Behavior

**States:** `idle`, `loading` (preview shown), `success` (preview and results), `error` (preview if there is one, and a message). A new image can be dropped from any state.

**Drop zone**
- Accept both drag and drop and click to browse, using a `<label>` around a file input so it works from the keyboard.
- Highlight the zone during drag-over.
- Call `preventDefault` on `dragover` and `drop` at the window level, so a drop that misses the zone doesn't make the browser navigate to the file.

**Checks before uploading.** These avoid wasted requests, and the server remains the authority.
- Type must be PNG, JPEG, WebP, GIF or BMP, matching the allowlist in `ColorApi/ImagePreparer.cs`.
- Size must be 20 MiB or less, matching the Kestrel limit in `ColorApi/Program.cs`.

**Request:** `FormData` with the file in the `image` field, sent to the relative URL `/color`.

**Superseded requests:** a new drop aborts the in-flight request with `AbortController`, and `AbortError` is ignored. This keeps a slow earlier response from overwriting a newer one. The endpoint already passes its cancellation token to the Jev call.

**Preview:** use `URL.createObjectURL`, and revoke the URL when the image is replaced or the component unmounts.

**Results:** show the closest color with a swatch and the confidence as a percentage. Below that, show one bar per entry in `probabilities`, keeping the API's highest-first order rather than re-sorting. Unknown color keys fall back to a neutral swatch and the raw key, so adding a color to the API doesn't break the UI.

**Error mapping** in `api.ts`. Read the body defensively, because a failure from Kestrel or the proxy may not be JSON.

| Response | Message shown |
|---|---|
| 400 | The problem response's `detail` if present (for example, the aspect-ratio message), otherwise "That image couldn't be read." |
| 413 | "Image is over the 20 MiB limit." |
| 502 with a problem `detail` | "The color model is unavailable. Try again." |
| Other status or network failure | "Couldn't reach the API. Is it running?" |

## Steps

1. Scaffold with `npm create vite@latest web -- --template react-ts`, and remove the template's demo content.
2. Add `test-results/` and `playwright-report/` to `web/.gitignore`, and commit `web/package-lock.json`.
3. In `vite.config.ts`, proxy `/color` to `process.env.COLOR_API_URL ?? 'http://localhost:5000'`.
4. Write `api.ts` and `colors.ts`, then `DropZone`, `Results` and `App`.
5. Add Vitest, jsdom and React Testing Library, with an `npm test` script and the component tests below. Exclude `e2e/` from Vitest.
6. Add `ColorApi/StubClassifier.cs` and the E2E registration in `Program.cs`, with its .NET tests.
7. Add Playwright (Chromium only), a `test:e2e` script and `e2e/color.spec.ts`.
8. Update `CLAUDE.md` and `README.md`:
   - Overview and the commands for running both processes and each test suite.
   - Under "Changes that span files", add `web/src/api.ts` to **Response format**, `web/src/colors.ts` to **Color options**, and a note that the accepted formats and size limit are duplicated in `web/src/api.ts`.

## Tests

### Component tests

These mock `fetch`, so they never call the API or TypeSafe:

| Case | Expected result |
|---|---|
| Valid image dropped | Sends `image` in `FormData`, then shows the closest color and all bars |
| Unsupported type or file over 20 MiB | Shows a message, `fetch` not called |
| Several files dropped | Uploads only the first, and says so |
| 400 with `detail` | Shows the `detail` text |
| 413, 502, non-JSON body, network failure | Shows the mapped message |
| Second drop while the first is in flight | First request aborted, only the second result shown |
| Unknown color key | Renders with the raw key |

### Integration tests (Playwright)

These run the real UI through the Vite proxy against the real API, including `ImagePreparer`, so they catch mismatches in the route, the `image` field and the JSON shape. Only the Jev call is replaced, so they spend no credits. Real model behavior stays covered by `test-images.sh` and the manual checks.

| Case | Expected result |
|---|---|
| Drop a valid image | Shows the stub's closest color and one bar per color |
| Drop a 2000x100 image | API returns 400, and its aspect-ratio message is shown |

**Stub classifier**
- `Program.cs` registers `StubClassifier` instead of `JevClassifier` only when `builder.Environment.IsEnvironment("E2E")`. This is the first API code change the UI needs.
- ASP.NET Core defaults to the `Production` environment, so the stub can't be used unless `ASPNETCORE_ENVIRONMENT=E2E` is set explicitly.
- New .NET tests check which classifier gets resolved: `JevClassifier` in `Production`, and `StubClassifier` in `E2E`.

**`playwright.config.ts`** starts both servers:
- API: `dotnet run --project ../ColorApi --urls http://localhost:5097`, with `ASPNETCORE_ENVIRONMENT=E2E` and `TYPESAFE_API_KEY=e2e-dummy-key`. Real environment variables win over `.env`, so the real key isn't loaded, and a stub failure couldn't spend credits.
- UI: `npm run dev`, with `COLOR_API_URL=http://localhost:5097`.
- `reuseExistingServer: false` for both, so a run can't silently attach to an API that's using the real classifier. If either port is in use, the run fails.

**Test images and drops:** each test generates its image on a canvas in the page, so it needs no fixture files. Playwright can't perform a drag from the operating system, so the test builds a `DataTransfer` with the file and dispatches `dragenter`, `dragover` and `drop` on the zone.

## Verification

```bash
npm --prefix web test             # component tests; no API key or API calls
npm --prefix web run build        # type-checks and builds

(cd web && npx playwright install chromium)   # first run only
npm --prefix web run test:e2e     # real UI and API with the stub; no API key or credits
dotnet test ColorApi.Tests        # includes the classifier registration tests

dotnet run --project ColorApi     # terminal 1
npm --prefix web run dev          # terminal 2, then open the printed URL
```

**Port 5000:** the API has no `launchSettings.json`, so it defaults to port 5000, which macOS's AirPlay Receiver can also occupy. If the API can't bind it or requests hit the wrong service, run:

```bash
dotnet run --project ColorApi --urls http://localhost:5098
COLOR_API_URL=http://localhost:5098 npm --prefix web run dev
```

Manual checks in a browser. Only the first and last rows of the table make a TypeSafe call that spends credits, so the first is limited to two images:

| Case | Expected result |
|---|---|
| Drop `ColorApi/test-files/red.jpg`, then `blue.jpg` | Correct color each time, with bars summing to about 100% |
| Drop a `.txt` or `.svg` file | Rejected in the browser, nothing in the Network tab |
| Drop an image outside the zone | Browser stays on the page |
| Stop the API, then drop an image | "Couldn't reach the API" message |
| Start the API with an invalid key | 502 message, with no upstream details shown |

## Out of scope

- Production hosting. When it comes up, the options are serving `web/dist` from the API's `wwwroot` (same origin, no CORS) or hosting separately and adding CORS.
- Authentication and rate limiting. `docs/plans/crayon-api-conversion.md` already lists these as required before public deployment, and a public UI would make them more urgent.
- Batch uploads, upload history, and HEIC support (not accepted by the API).
