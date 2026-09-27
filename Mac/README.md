# ElevenLabs Music Generator for Mac

The Mac app uses standard macOS menus and stores your ElevenLabs API key in Keychain. It requires macOS 14 or later. Download the Apple Silicon ZIP for an M-series Mac or the Intel ZIP for an Intel Mac from the project's releases page, extract the app, and drag it to Applications.

On first use, open Settings to enter and test your API key. Enable the key's `user_read` permission if you want the app to show your subscription balance; an existing key can be edited to grant it. The first generation creates `~/Music/ElevenLabs Music Generator`; you can choose another folder in Settings. The app asks for confirmation before requests that may spend credits.

The balance field refreshes when the app opens and after generation. Option-B focuses its read-only text; Command-R refreshes it manually if credits change elsewhere. It reads the included subscription allowance and next reset without generating audio, and reports overage billing when available. It cannot determine the total spendable balance, a per-request charge, or a separate limit placed on an API key.

Open Plan or Details can reload a saved plan or generated track details containing a reusable composition plan. Details without a plan are optional records and are not required to play the audio.

Read the [Mac manual](Manual.html) for composition plans, keyboard commands, recovery, file locations, and manual update instructions.

Choose Sound Effects v2 in the model selector to generate effects instead of music. Enable Sound Effects access on your API key. Effects support automatic or fixed duration (0.5 to 30 seconds), looping, and prompt influence. Generated effects use the same output folder, with a reusable `.sfx.json` prompt that you can reload with Open Prompt. Music plans and lyrics do not apply to effects.
