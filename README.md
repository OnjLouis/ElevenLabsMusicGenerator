# ElevenLabs Music Generator

An accessible, portable Windows application for generating music through the ElevenLabs Music API.

## Design

- Standard Windows Forms controls with keyboard and screen-reader support.
- Direct HTTPS communication with the ElevenLabs REST API.
- No Python, bundled runtime, registry settings, installation, or startup extraction.
- Portable settings beside the executable and private changing data under `User`.
- A relative `Music` folder beside the executable for new installs; custom output folders are selected in Preferences.
- Manually saved prompts and generated audio use the selected output folder.
- Atomic audio output, cancellation cleanup, draft recovery, and one shared prompt file per generation batch.
- Interrupted batches resume only missing variations after checking existing tracks.

## Building

Use Windows PowerShell and provide a new staging directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -OutputRoot C:\Path\To\New\Staging
```

The build uses the C# compiler supplied with .NET Framework and writes all output outside the source tree. Pass an external RSA XML private key through `-SigningKeyPath` to create the signature required by the updater. Private keys must never be stored in the repository, source archive, portable folder, or release package.

## Privacy

The API key is stored in `User\ApiKey.txt`. It must never be committed or included in a package. Prompts and generated music are sent to ElevenLabs only when the user confirms generation.
