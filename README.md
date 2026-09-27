# ElevenLabs Music and Sound FX Generator

Accessible Windows and Mac applications for generating music and sound effects through ElevenLabs. The two platforms have separate downloads and store settings independently.

## Getting started

You need an ElevenLabs account with Music API access and sufficient credits. Create an API key in ElevenLabs under Developers, API Keys, and enable Music access. Enable `user_read` as well if you want the app to show your subscription balance; an existing key can be edited to grant it. You can set a credit limit for safety. In the app, open Preferences or Settings, save the key, and use Test Key before generating music. Key testing does not generate music.

Write a prompt, choose the model, format, duration and number of variations, then review the confirmation before spending credits. Music v2 and v2.5 also support editable composition plans with sections and lyrics. Generated audio, the shared prompt, and optional returned lyrics and details are saved in the chosen Music folder. An interrupted batch can resume missing variations without overwriting completed tracks.

The balance field refreshes when the app opens and after generation. Alt+B focuses its read-only text; F5 refreshes it manually if credits change elsewhere. It reads the included subscription allowance and next reset without generating audio, and reports overage billing when available. It cannot determine the total spendable balance, a per-request charge, or a separate limit placed on an API key.

The base filename keeps spaces you type. Clear it before generating Music to name each variation from the title returned by ElevenLabs. A small record in the output folder's `Lyrics` subfolder makes these titled batches resumable. Prompts show a mode-specific character counter and stop at the API limit.

Saved `.plan.json` files can be reopened with Open Prompt or Plan or Open Plan or Details. If a generated track's optional `.details.json` contains a composition plan, either command loads it for editing and reuse. Saved plans, sound effect prompts, and track details use readable multi-line JSON; escaped `\n` inside a JSON string becomes a lyric line break when imported. Details without a reusable plan remain a record of information returned by ElevenLabs; they are not required for audio playback.

## Platform downloads

Choose Sound Effects v2 in the model selector to generate effects instead of music. Enable Sound Effects access on your API key. Effects support automatic or fixed duration (0.5 to 30 seconds), looping, and prompt influence. Generated effects use the same output folder, with a reusable `.sfx.json` prompt that you can reload with Open Prompt. Music plans and lyrics do not apply to effects.

- Windows: download the Windows ZIP, extract it to a folder, and run `ElevenLabsMusicGenerator.exe`. By default, music is saved in a `Music` folder beside the app. Preferences and drafts stay with the portable app; the API key is protected for the current Windows account and computer.
- Mac: download the Apple Silicon ZIP for an M-series Mac or the Intel ZIP for an Intel Mac, extract the app, and drag it to Applications. The first save uses `~/Music/ElevenLabs Music Generator`. The API key is stored in your Mac Keychain. Check for Updates checks published versions for a matching Mac package and offers the releases page; installation is manual.

Do not share personal settings, prompts, music, drafts, or API-key files when sharing the application. The app sends your prompt or composition plan to ElevenLabs when you confirm generation.

The [Windows manual](Manual.html) and [Mac manual](Mac/Manual.html) describe the controls, keyboard commands, file locations, recovery, and update behavior for each platform.
