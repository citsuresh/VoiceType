VoiceType — Whisper models folder
==================================

This folder must contain a Whisper GGML model file (*.bin) for speech
recognition to work. Model files are NOT included with the app because they
are too large to store in the source repository.

HOW TO GET A MODEL
------------------
1. Download a GGML model (.bin) from the official whisper.cpp repository:

     https://huggingface.co/ggerganov/whisper.cpp

   Direct link pattern:
     https://huggingface.co/ggerganov/whisper.cpp/resolve/main/<filename>

   (If Hugging Face is blocked on your network, download it elsewhere and
    copy the .bin file into this folder.)

2. Recommended models (pick one):

     ggml-base.en.bin              ~142 MB   fastest, lowest RAM, least accurate
     ggml-small.en.bin             ~466 MB   best accuracy-per-MB for English
     ggml-large-v3-turbo-q5_0.bin  ~547 MB   near-best accuracy (latest model)

3. Place the .bin file in THIS folder.

4. Point the app at it in appsettings.json:

     "WhisperModelPath": "./whisper.cpp/models/ggml-small.en.bin"

CHOOSING FOR YOUR RAM
---------------------
- Server mode keeps the model loaded in memory for fast responses.
- CLI mode loads the model only during transcription, then frees it.
- On low-RAM machines, prefer a smaller model (base.en / small.en) OR use a
  large model with CLI mode instead of Server mode.

See the main README.md in the repository root for full details.
