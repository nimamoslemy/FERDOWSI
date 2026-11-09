#pragma once
#ifndef GRAMMERV_NATIVE_H
#define GRAMMERV_NATIVE_H

#include <windows.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#ifdef GRAMMERV_EXPORTS
#define GV_API __declspec(dllexport)
#else
#define GV_API __declspec(dllimport)
#endif

#pragma pack(push, 8)
typedef struct GvMonitorState {
    int32_t caret_x;
    int32_t caret_y;
    int32_t caret_w;
    int32_t caret_h;
    int32_t caret_source;         // 0=none, 1=Win32 GUIThreadInfo, 2=MSAA Caret, 4=IBeam/Selection Anchor
    int32_t mouse_x;
    int32_t mouse_y;
    int32_t is_ibeam_cursor;      // 1 if current cursor is text I-Beam
    int32_t has_selection_event;  // 1 if recent mouse drag/double-click selection occurred
    int32_t is_typing_active;     // 1 if user typed recently in current focused window
    int32_t typed_char_count;     // number of chars in live typed sentence buffer
    uint64_t last_type_tick;      // GetTickCount64() of last keystroke
    uint64_t last_select_tick;    // GetTickCount64() of last mouse selection
    int64_t foreground_hwnd;      // active foreground HWND
    int64_t focus_hwnd;           // focused child control HWND (if available)
    int32_t is_password_focused;  // 1 if focused element is a password input field (suppresses all capture)
} GvMonitorState;
#pragma pack(pop)

// Lifecycle & hook thread management
GV_API int32_t gv_init(void);
GV_API void    gv_shutdown(void);
GV_API void    gv_set_own_windows(int64_t overlay_hwnd, int64_t studio_hwnd);
GV_API void    gv_set_password_mode(int32_t is_password);

// Poll current monitor, caret, selection, and typing state
GV_API int32_t gv_poll_state(GvMonitorState* out_state);

// Read or clear live typed text buffer captured by native low-level keyboard tracker
GV_API int32_t gv_get_typed_buffer(wchar_t* out_buf, int32_t max_chars);
GV_API void    gv_clear_typed_buffer(void);
GV_API void    gv_ack_selection_event(void);

// Safe clipboard-preserving selection copy, full-field copy & direct Unicode text replacement
// replace_mode: 0 = replace current selection (VK_BACK + direct KEYEVENTF_UNICODE stream)
//               1 = backspace N live-typed chars + direct KEYEVENTF_UNICODE stream
//               2 = select current line (Home, Shift+End) + direct KEYEVENTF_UNICODE stream
//               3 = select full field (Ctrl+A) + direct KEYEVENTF_UNICODE stream
GV_API int32_t gv_capture_selection(int64_t target_hwnd, wchar_t* out_buf, int32_t max_chars);
GV_API int32_t gv_capture_full_field(int64_t target_hwnd, wchar_t* out_buf, int32_t max_chars);
GV_API int32_t gv_replace_text(int64_t target_hwnd, const wchar_t* new_text, int32_t replace_mode, int32_t backspace_count);
GV_API int32_t gv_insert_text_at_cursor(int64_t target_hwnd, const wchar_t* text);

// Native Windows Microphone Voice Recording (16kHz 16-bit mono PCM WAV for Deepgram Voice / Aura API)
GV_API int32_t gv_voice_start_recording(void);
GV_API int32_t gv_voice_stop_recording(const wchar_t* out_wav_path);
GV_API int32_t gv_voice_is_recording(void);

// High-speed native C++20 Damerau-Levenshtein & Windows OS ISpellChecker COM engine
GV_API int32_t gv_damerau_distance(const wchar_t* a, const wchar_t* b);
GV_API int32_t gv_is_valid_word(const wchar_t* word);
GV_API int32_t gv_spell_check_word(const wchar_t* word, wchar_t* out_suggestion, int32_t max_chars);

// Windows Copilot+ NPU Phi Silica (Microsoft.Windows.AI.Text.LanguageModel / TextRewriter)
// Returns AIFeatureReadyState: 0=Ready, 1=NotReady, 2=NotSupported, 3=Disabled, 4=CapabilityMissing, -1=Unavailable
GV_API int32_t gv_phi_silica_status(void);
// mode: 0=custom prompt/grammar fix, 1=General/Fluent, 2=Casual, 3=Concise, 4=Formal
GV_API int32_t gv_phi_silica_rewrite(const wchar_t* text, int32_t tone_mode, const wchar_t* custom_instruction,
                                     wchar_t* out_buf, int32_t max_chars, int32_t* out_elapsed_ms);

#ifdef __cplusplus
}
#endif

#endif // GRAMMERV_NATIVE_H
