# CrayonCli

A prototype CLI that sends an image to TypeSafe's Jev model and asks which of the
8 classic crayon colors (red, orange, yellow, green, blue, violet, brown, black)
it's closest to. Prints the pick plus the probability for each color.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A TypeSafe API key

## Setup

`dotnet run` restores the NuGet packages automatically.

```bash
cd CrayonCli
echo 'TYPESAFE_API_KEY=...' > .env       # or export it; a real environment variable wins
dotnet run -- test-files/red.jpg
```

## Testing

```bash
CrayonCli/test-images.sh
```

Runs every image in `CrayonCli/test-files/` through the CLI and checks that Jev picks the color
in the filename. This makes one TypeSafe API call per image, currently 8 in total.

Exit codes: 0 all passed, 1 at least one wrong pick or error, 2 build failed.

## Notes

- Image is auto-oriented (Jev ignores EXIF), scaled into 280–1,568 px per side, re-encoded as PNG.
- Image input is experimental on TypeSafe's side ("danger zone"); the SDK sends the opt-in header.
- Three lines in `CrayonCli/Program.cs` are marked `VERIFY` (`AddChoice`, `answer.Choice`, `answer.Probabilities`).
  They compile against the SDK version in `CrayonCli/CrayonCli.csproj`, but their results still need checking in a live run.
- Exit codes: 0 ok, 1 local error, 2 TypeSafe API error (400 bad image, 413 too large).

## Ideas

- `--local` mode: nearest crayon color in Lab space as a free baseline to compare against Jev.
- Wrap the core in an ASP.NET minimal API endpoint.

## License

The code is available under the [MIT License](LICENSE). This doesn't cover the images in
`CrayonCli/test-files/`, which keep their own licenses; see [ATTRIBUTION.md](CrayonCli/test-files/ATTRIBUTION.md).
