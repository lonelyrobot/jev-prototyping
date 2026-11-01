# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ColorApi is a prototype .NET 10 Minimal API. Its one endpoint, `POST /color`, sends an uploaded image to TypeSafe's Jev model and returns which of 8 crayon colors it is closest to, along with the probability for each color, as JSON. The code is in `ColorApi/`, and the unit tests are in `ColorApi.Tests/`.

## Commands

Run these from the repo root. Everything except `dotnet test` and `dotnet build` needs `TYPESAFE_API_KEY`, set in the environment or in a `.env` file; a real environment variable takes precedence.

```bash
dotnet build ColorApi           # no API call
dotnet test ColorApi.Tests      # unit tests; no API key or API calls
dotnet run --project ColorApi   # start the API, then upload an image (one API call):
curl -F "image=@ColorApi/test-files/red.jpg" http://localhost:5000/color
ColorApi/test-images.sh         # start the API and run all test images (one API call per image, currently 8)
```

To run a single unit test, use `dotnet test ColorApi.Tests --filter <TestName>`. There is no lint configuration.

`dotnet run` only finds `.env` if it's in the current directory or a parent, so run from `ColorApi/` if that's where it is.

## How it works

- **`Program.cs`:** startup and the endpoint. It runs `ImagePreparer`, calls the classifier, sorts the probabilities and maps errors to HTTP statuses.
- **`ImagePreparer.cs`:** treats uploads as untrusted. It checks the format and pixel count before decoding, auto-orients the image (Jev ignores EXIF), scales it to 280–1,568 px per side, and re-encodes it as PNG. It throws `BadImageException` if the aspect ratio is too extreme to fit both limits.
- **`JevClassifier.cs`:** implements `IColorClassifier`. It attaches the image to `RequestOptions`, asks a single Choice question listing the crayon colors, and sends it to the pinned model (`Model` constant). It converts `TypeSafeApiException` to `ClassifierException`.

HTTP statuses: 200 ok, 400 bad image, 413 upload over 20 MiB, 502 TypeSafe API error (details are only logged). A missing API key stops the app at startup. `test-images.sh` exit codes: 0 all passed, 1 at least one wrong pick or error, 2 build or server startup failed.

## Testing

- **Unit tests:** these replace `IColorClassifier` with `FakeClassifier` through `WebApplicationFactory`, so they never call TypeSafe. They generate images in memory and don't use `test-files/`.
- **`test-images.sh`:** the only check of real Jev behavior, so results depend on the model. It needs `curl` and `jq`, and uses port 5099 unless `COLOR_TEST_PORT` is set.
- **Upload size limit:** this is a Kestrel setting that `WebApplicationFactory`'s test server doesn't apply, so unit tests can't cover it.

## Changes that span files

- **Response format:** `test-images.sh` reads `.closest` and `.confidence` from the JSON, and the endpoint tests check the same fields. Renaming a `ColorResult` property can break both.
- **Color options:** the script compares each image's filename to the pick, so `test-files/<color>.jpg` must match the option key in the `ChoiceQuestion` dictionary. Only `.jpg` files are picked up.
- **Test images:** these aren't covered by the repo's MIT license. Credit any added image in `ColorApi/test-files/ATTRIBUTION.md`.

## SDK caveats

- `tryAGI.TypeSafeAI` is a community SDK. SDK docs: https://tryagi.github.io/TypeSafeAI/
- The Web SDK imports `System.Net.Http.Json`, which also defines `JsonContent`, so `JevClassifier.cs` needs its alias for the TypeSafe type.
- Image input is experimental on TypeSafe's side ("danger zone"). The SDK sends the required opt-in header.
