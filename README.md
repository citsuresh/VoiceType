# VoiceType

A local, privacy-preserving push-to-talk dictation tool for Windows. Hold a hotkey,
speak, and the transcribed text is inserted at your cursor. All speech recognition runs
**locally** using [whisper.cpp](https://github.com/ggml-org/whisper.cpp) — nothing is sent
to the cloud.

---

## Speech recognition models (required — download separately)

The Whisper model files (`*.bin`) are **not stored in this repository** because they exceed
GitHub's 100 MB per-file limit. You must download a model manually and place it in the
models folder before the app can transcribe.

### 1. Where to download

Download the pre-converted **GGML** models from the official whisper.cpp model repository on
Hugging Face:

- **Repository:** `ggerganov/whisper.cpp` (mirror: `ggml-org/whisper.cpp`)
- **Direct URL pattern:** `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/<filename>`

> Only GGML `.bin` files work with whisper.cpp. Do **not** use `openai/whisper-*` or
> `Systran/faster-whisper-*` repos — those are PyTorch / CTranslate2 formats for other engines.
>
> If Hugging Face is blocked on your network, download on another machine/network and copy the
> `.bin` file over. Prefer the official repo over random re-uploads for integrity.

### 2. Which model to pick

| Model file | Disk size | Resident RAM (approx.) | Accuracy | Notes |
|------------|-----------|------------------------|----------|-------|
| `ggml-base.en.bin` | ~142 MB | ~210 MB | Baseline | Fastest, least accurate. Good for low-RAM machines. |
| `ggml-small.en.bin` | ~466 MB | ~550–650 MB | Big step up | **Best accuracy-per-MB for English dictation.** |
| `ggml-large-v3-turbo-q5_0.bin` | ~547 MB | ~800 MB–1.1 GB | Near-best | Latest Whisper model (Oct 2024). Use CLI mode if RAM is tight. |

`large-v3-turbo` is the newest OpenAI Whisper model; there is no v4. The `-q5_0` suffix is a
quantized (smaller/faster) build with negligible accuracy loss.

### 3. Where to place it

Copy the downloaded `.bin` into the **source** models folder:

```
VoiceType/VoiceType/whisper.cpp/models/
```

The build copies model files to the output folder automatically. After placing the file,
rebuild so it lands in `bin/.../whisper.cpp/models/`.

### 4. Point the app at your model

Edit [`VoiceType/VoiceType/appsettings.json`](VoiceType/VoiceType/appsettings.json) and set
`WhisperModelPath` to your file:

```json
"WhisperModelPath": "./whisper.cpp/models/ggml-small.en.bin"
```

---

## Transcription modes

Set `"Mode"` in `appsettings.json`. All modes run the same engine and produce the same
accuracy for a given model — they differ in **latency and memory profile**.

| Mode | How it works | Latency | Idle RAM | Best for |
|------|--------------|---------|----------|----------|
| `Server` | Long-lived `whisper-server.exe` keeps the model loaded; each utterance is an HTTP call | **Lowest** (no reload) | Model stays resident | Frequent dictation, fast response |
| `Cli` | Spawns `whisper-cli.exe` per utterance; loads model, transcribes, exits | Higher (reload each time) | ~0 (freed after each use) | Occasional dictation, low-RAM machines, large models |
| `WavFile` | Records to WAV, then transcribes with the CLI executable | Higher | ~0 | Simple fallback |
| `Stream` | Real-time `whisper-stream.exe` (sliding window) | Low | Model resident | **Not recommended** — produces repeated/corrected text |

If `Server` mode fails to start, the app automatically **falls back to CLI**.

### Server mode settings

```json
"WhisperServerExecutablePath": "./whisper.cpp/whisper/Release/whisper-server.exe",
"WhisperServerHost": "127.0.0.1",
"WhisperServerPort": 51234,
"WhisperServerArguments": "-bs 8 -bo 8 -mc 0"
```

`WhisperServerArguments` passes decoding flags to improve accuracy:

- `-bs 8` beam size (higher = more accurate, slower; also uses more RAM)
- `-bo 8` best-of candidates
- `-mc 0` max context = 0 (reduces repetition/hallucination for short dictation)

The port is in the private range (49152–65535) to avoid conflicts with common services on 8080.
The same flags work in CLI mode via `WhisperCliArguments`.

---

## Choosing settings for your RAM

**Check your available RAM first** (Task Manager → Performance → Memory). The *free* amount
matters more than the total.

| Your situation | Recommended setup |
|----------------|-------------------|
| **Plenty free (>3 GB)** | `large-v3-turbo-q5_0` + `Server` mode — best accuracy, fast |
| **Moderate (1.5–3 GB free), 16 GB total** | `small.en` + `Server` mode — big accuracy gain, ~600 MB resident |
| **Tight (<1.5 GB free)** | `base.en` + `Server`, **or** `large-v3-turbo` + **`Cli`** mode (loads only during transcription, frees after) |
| **Very constrained / infrequent use** | `base.en` + `Cli` mode — near-zero idle memory |

**Key trade-off:** Server mode holds the model in RAM permanently for speed. CLI mode uses
almost no idle RAM but reloads the model on every dictation (2–4 s extra for large models).
On a 16 GB machine that's already near capacity, avoid keeping a large model resident in
Server mode — either use a smaller model or switch that large model to CLI mode.

### Reducing memory of a large model
- Lower beam search: change `-bs 8 -bo 8` toward `-bs 1` (less RAM, slightly less accurate)
- Use a quantized build (`q5_0`, `q5_1`) instead of the full-precision model

---

## Hotkey & insertion

```json
"HotkeyModifiers": "Ctrl",
"HotkeyKey": "LeftAlt",
"InsertMethod": "Clipboard"
```

Hold **Ctrl + Left Alt**, speak, release. `InsertMethod` can be `Clipboard` (paste, default)
or `SendInput` (synthetic typing).

---

## Audio

Microphone is captured at **16 kHz mono 16-bit** — Whisper's native format, so no resampling
is needed. Speak close to the mic in a quiet environment for best accuracy.