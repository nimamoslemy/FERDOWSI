# FERDOWSI — Above-Cursor English, Persian & Finglish Writing Assistant, Voice Dictation & AI Dictionary

**FERDOWSI** (`FERDOWSI.exe`) is a system-wide Windows desktop writing assistant, voice dictation tool, and bilingual AI dictionary built with **C++20**, **Python 3**, **C# (WPF + Windows UI Automation)**, **Google Gemini API**, **DeepSeek API**, **Deepgram Aura / Nova-2 Speech-to-Text API**, and **Windows Copilot+ NPU Phi Silica** support.

GitHub: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)

---

## Key Features
- **Appears Above Your Cursor Anywhere (`WS_EX_NOACTIVATE`)**:
  - Works across Microsoft Edge, Chrome, Word, Notepad, Telegram, VS Code, Slack, Discord, and any Windows text box without stealing keyboard focus (`Alt + G` or `Ctrl + Shift + G`).
- **🔒 Automatic Password Protection**:
  - Automatically detects password boxes across Win32 (`ES_PASSWORD` / `EM_GETPASSWORDCHAR`), Windows UI Automation (`IsPassword`), and WPF `PasswordBox`, immediately wiping its typing buffer and hiding the floating window so your password is never revealed.
- **🎤 Write with Your Voice (Deepgram Speech-to-Text Above Cursor)**:
  - Click the **`🎤`** microphone icon on the small floating window above your cursor, speak naturally in English or Persian, and click **`🔴`** to stop — FERDOWSI transcribes your speech via Deepgram and types it directly at your cursor.
- **⏸ Close / Pause for a Period of Time (`10 min` / `30 min` / `1 hr`)**:
  - Click **`×`** on the small floating window above your cursor (or use Studio Settings / System Tray) to dismiss for the current box or pause the floating widget for **10 minutes (`10 min`)**, **30 minutes (`30 min`)**, or **1 hour (`1 hr`)**.
- **✨ 'AI Only' Mode & Ultra-Fast Cloud AI (<0.8s)**:
  - Choose **`AI Only`** in AI Provider Priority so FERDOWSI uses exclusively Cloud AI (**Google Gemini** or **DeepSeek**) and never falls back to Python offline rules.
  - Uses persistent HTTP/TLS Keep-Alive connection pooling + background connection warm-up + `thinkingBudget: 0` on Gemini Flash-Lite models to reduce AI Format/Reword/Correct latency from ~4s down to **~0.4–0.8s**.
- **✓ Correct (English, Persian `فارسی` & Finglish)**:
  - Fixes English & Persian spelling, grammar, homophones, and Persian Zero-Width Non-Joiners (`نیم‌فاصله`, e.g., `می‌روم`, `خانه‌ها`).
  - Converts **Finglish** directly into standard Persian script (e.g., `salam halet chetore` → `سلام، حالت چطوره؟`).
- **✨ Reword (6 Tones) & Aa Format (12 Smart Formatters)**:
  - Rewrite in `Fluent`, `Formal`, `Casual`, `Concise`, `Expand`, and `Persuasive` tones, or format into `Bullet List`, `Numbered List`, `Email Template`, `Finglish → فارسی`, `نیم‌فاصله فارسی`, `Title Case`, `snake_case`, and `camelCase`.
- **📖 AI Dictionary (Persian Translations, Synonyms, Antonyms & Examples)**:
  - Look up any single word or full sentence to view Persian translations, part of speech, phonetic IPA, clickable Synonyms (`Syn`) & Antonyms (`Ant`), and bilingual example sentences.

---

## How to Get Your API Keys (Google Gemini, DeepSeek & Deepgram)
To use Cloud AI features (`AI Only` mode, full-sentence rewording, AI Dictionary, and Voice Writing), add an API key in **FERDOWSI Studio Settings**:
1. **Google Gemini API Key (Free tier available)**:
   - Click **`Get Gemini Key ↗`** ([aistudio.google.com/apikey](https://aistudio.google.com/apikey)), sign in with your Google account, click **Create API Key**, paste it into FERDOWSI, and click **`⚡ Save & Test Google Gemini`**.
2. **DeepSeek API Key**:
   - Click **`Get DeepSeek Key ↗`** ([platform.deepseek.com/api_keys](https://platform.deepseek.com/api_keys)), create an API key, paste it into the DeepSeek box, and click **`⚡ Save & Test DeepSeek API`**.
3. **Deepgram Voice API Key ($200 free credit on signup)**:
   - Click **`Get Deepgram Key ↗`** ([console.deepgram.com](https://console.deepgram.com/)), create a free account and API key, paste it into the Deepgram Voice box, and click **`🎤 Save & Test Deepgram Voice`**.

> **Privacy & Security**: Your API keys are stored **strictly in your private Windows profile** (`%APPDATA%\FERDOWSI\settings.json`) and are never bundled in the application folder or installer.

---

## Build & Test
```powershell
# Build grammerv_native.dll + standalone FERDOWSI.exe
powershell -ExecutionPolicy Bypass -File .\build.ps1

# Package standalone installer (FERDOWSI_Installer)
powershell -ExecutionPolicy Bypass -File .\package_installer.ps1

# Run automated test suite & security audit
python .\tests\test_all.py
python .\tests\security_audit.py
```

---

## GitHub
GitHub: [https://github.com/nimamoslemy](https://github.com/nimamoslemy)
