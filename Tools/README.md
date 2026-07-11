Whisper setup helper
====================

This folder contains a PowerShell helper script to download a prebuilt whisper.cpp executable for Windows and a ggml model file.

Usage
-----

From the repository root run:

    powershell -ExecutionPolicy Bypass -File .\tools\get-whisper.ps1

The script will attempt to download the latest whisper.cpp release asset for Windows and the ggml-base.bin model into ./whisper and ./models.

After download update VoiceType\VoiceType\appsettings.json if necessary (the defaults point to ./whisper/whisper.exe and ./models/ggml-base.bin).
