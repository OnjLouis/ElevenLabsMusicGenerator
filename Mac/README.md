# ElevenLabs Music Generator for Mac

The Mac app uses standard macOS menus and stores your ElevenLabs API key in Keychain. It requires macOS 14 or later. Download the Apple Silicon ZIP for an M-series Mac or the Intel ZIP for an Intel Mac from the project's releases page, extract the app, and drag it to Applications.

On first use, open Settings to enter and test your API key. Enable the key's `user_read` permission if you want the app to show your subscription balance; an existing key can be edited to grant it. The first generation creates `~/Music/ElevenLabs Music Generator`; you can choose another folder in Settings. The app asks for confirmation before requests that may spend credits.

The balance field refreshes when the app opens and after generation. Option-B focuses its read-only text; Command-R refreshes it manually if credits change elsewhere. It reads the included subscription allowance and next reset without generating audio, and reports overage billing when available. It cannot determine the total spendable balance, a per-request charge, or a separate limit placed on an API key.

Open Prompt or Open Plan or Details can reload a saved plan or generated track details containing a reusable composition plan. Saved plans, sound effect prompts, and track details use readable multi-line JSON; escaped `\n` inside a JSON string becomes a lyric line break when imported. Details without a plan are optional records and are not required to play the audio.

Read the [Mac manual](Manual.html) for composition plans, keyboard commands, recovery, file locations, and manual update instructions.

Choose Sound Effects v2 in the model selector to generate effects instead of music. Enable Sound Effects access on your API key. Effects support automatic or fixed duration (0.5 to 30 seconds), looping, and prompt influence. Generated effects use the same output folder, with a reusable `.sfx.json` prompt that you can reload with Open Prompt. Music plans and lyrics do not apply to effects.

Test Key checks the selected feature and separately reports whether the key can read the credit balance (`user_read`), even if the feature test fails. Music v2 and v2.5 use their respective non-generating plan requests; Music v1 access is checked through a v2.5 plan request. The Sound Effects test requires confirmation and generates a disposable 0.5-second effect that may spend credits.
