# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

CrayonCli is a prototype .NET 10 CLI that sends an image to TypeSafe's Jev model and prints which of 8 crayon colors it is closest to, along with the probability for each color. All of the code is in `CrayonCli/Program.cs`.

## Commands

Run these from `CrayonCli/`. `TYPESAFE_API_KEY` must be set in the environment or in a `.env` file; a real environment variable takes precedence.

```bash
dotnet build                     # no API call
dotnet run -- test-files/red.jpg # run one image (one API call)
./test-images.sh                 # run all test images (one API call per image, currently 8)
```

There is no unit test project or lint configuration. `test-images.sh` is the only test suite, and it calls the live API, which makes results dependent on the model. To test a single case, run one image with `dotnet run` as shown above.

## How it works

`Program.cs` is a top-level program with a linear flow:

1. `PrepareImage` auto-orients the image (Jev ignores EXIF), scales it to 280–1,568 px per side, and re-encodes it as PNG. It throws if the aspect ratio is too extreme to fit both limits.
2. The image is attached to `RequestOptions` and referenced from the JSON state.
3. A single Choice question lists the crayon colors, and `client.SystemOneAsync` sends it to the pinned model (`Model` constant).
4. The pick and the sorted probabilities are printed to stdout.

CLI exit codes: 0 ok, 1 local error, 2 TypeSafe API error. `test-images.sh` uses different codes: 0 all passed, 1 at least one wrong pick or error, 2 build failed.

## Changes that span files

- **Output format:** `test-images.sh` greps for a line starting with `Closest crayon:` and takes the third whitespace-separated field as the pick. Changing that line in `Program.cs` can break the script.
- **Color options:** the script compares each image's filename to the pick, so `test-files/<color>.jpg` must match the option key in the `ChoiceQuestion` dictionary. Only `.jpg` files are picked up.
- **Test images:** these aren't covered by the repo's MIT license. Credit any added image in `CrayonCli/test-files/ATTRIBUTION.md`.

## SDK caveats

- `tryAGI.TypeSafeAI` is a community SDK. Three lines in `Program.cs` are marked `VERIFY` (`AddChoice`, `answer.Choice`, `answer.Probabilities`): they compile against the version in `CrayonCli.csproj`, but their results still need checking in a live run. SDK docs: https://tryagi.github.io/TypeSafeAI/
- Image input is experimental on TypeSafe's side ("danger zone"). The SDK sends the required opt-in header.
