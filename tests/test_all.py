#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""End-to-end automated test & latency benchmark suite for FERDOWSI (C++ + Python + C# EXE)."""

import ctypes
import os
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "src", "python"))


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    t_start = time.perf_counter()
    print("=== FERDOWSI Tri-Language & AI Automated Test Suite ===")

    dll_path = os.path.join(ROOT, "grammerv_native.dll")
    exe_path = os.path.join(ROOT, "FERDOWSI.exe")
    flag_path = os.path.join(ROOT, "assets", "ferdowsi_flag.png")
    assert os.path.isfile(dll_path), f"Missing {dll_path}"
    assert os.path.isfile(exe_path), f"Missing {exe_path}"
    assert os.path.isfile(flag_path), f"Missing {flag_path}"
    os.environ["GRAMMERV_DLL_PATH"] = dll_path

    # 1. Test C++ Native DLL
    t0 = time.perf_counter()
    dll = ctypes.WinDLL(dll_path)
    dll.gv_damerau_distance.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
    dll.gv_damerau_distance.restype = ctypes.c_int32
    assert dll.gv_damerau_distance("cursur", "cursor") == 1
    assert dll.gv_damerau_distance("teh", "the") == 1

    dll.gv_spell_check_word.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_int32]
    dll.gv_spell_check_word.restype = ctypes.c_int32
    buf = ctypes.create_unicode_buffer(64)
    assert dll.gv_spell_check_word("grammer", buf, 64) == 1 and buf.value == "grammar"
    assert dll.gv_spell_check_word("requiered", buf, 64) == 1 and buf.value == "required"
    # Verify common short valid words have ZERO false positives
    assert dll.gv_spell_check_word("went", buf, 64) == 0
    assert dll.gv_spell_check_word("make", buf, 64) == 0

    dll.gv_phi_silica_status.argtypes = []
    dll.gv_phi_silica_status.restype = ctypes.c_int32
    phi_state = dll.gv_phi_silica_status()

    # Verify full-field browser/editor capture, Unicode direct replacement, password shield & voice exports exist
    assert hasattr(dll, "gv_capture_full_field"), "Missing gv_capture_full_field export"
    assert hasattr(dll, "gv_replace_text"), "Missing gv_replace_text export"
    assert hasattr(dll, "gv_set_password_mode"), "Missing gv_set_password_mode export"
    assert hasattr(dll, "gv_insert_text_at_cursor"), "Missing gv_insert_text_at_cursor export"
    assert hasattr(dll, "gv_voice_start_recording"), "Missing gv_voice_start_recording export"
    assert hasattr(dll, "gv_voice_stop_recording"), "Missing gv_voice_stop_recording export"
    assert hasattr(dll, "gv_voice_is_recording"), "Missing gv_voice_is_recording export"

    # Verify password mode wipes typed buffer immediately
    dll.gv_set_password_mode.argtypes = [ctypes.c_int32]
    dll.gv_set_password_mode.restype = None
    dll.gv_get_typed_buffer.argtypes = [ctypes.c_wchar_p, ctypes.c_int32]
    dll.gv_get_typed_buffer.restype = ctypes.c_int32
    dll.gv_set_password_mode(1)
    tbuf = ctypes.create_unicode_buffer(128)
    assert dll.gv_get_typed_buffer(tbuf, 128) == 0 and tbuf.value == ""
    dll.gv_set_password_mode(0)
    cpp_ms = (time.perf_counter() - t0) * 1000.0
    print(f"[PASS] C++20 grammerv_native.dll + Password Shield + Voice WAV exports ({cpp_ms:.2f} ms) | Phi Silica ReadyState={phi_state}")

    # 2. Test Python NLP Engine (including new passive/agreement rules & Google Gemini configuration bridge)
    import nlp_engine

    t0 = time.perf_counter()
    sample = "make an app just like grammerly which will be shown up when i want to type in anywhere above my cursur and help me to reword or correct or changing the format of my words or sentence if it requiered"
    res = nlp_engine.handle_request({"id": 1, "action": "analyze", "text": sample, "fast_badge_only": True})
    assert res["ok"] and res["issue_count"] >= 6
    assert "Grammarly" in res["corrected"]
    assert "cursor" in res["corrected"]
    assert "required" in res["corrected"]
    print(f"[PASS] Python+C++ Grammar Analysis ({res['elapsed_ms']:.2f} ms): {res['issue_count']} fixes -> {res['corrected']}")

    edge_sample = "It seems that it can not get the full sentence and a few words will be remained untouched when it delete the sentence"
    res_edge = nlp_engine.handle_request({"id": 10, "action": "analyze", "text": edge_sample, "fast_badge_only": True})
    assert res_edge["ok"]
    assert "cannot" in res_edge["corrected"]
    assert "will remain" in res_edge["corrected"]
    assert "when it deletes" in res_edge["corrected"]
    print(f"[PASS] Full-Sentence Passive/Agreement Fix ({res_edge['elapsed_ms']:.2f} ms): {res_edge['issue_count']} fixes -> {res_edge['corrected']}")

    chat_sample = "Ok, what dddo you meaan if I chec k this for you."
    res_chat = nlp_engine.handle_request({"id": 12, "action": "analyze", "text": chat_sample, "fast_badge_only": True})
    assert res_chat["ok"] and res_chat["issue_count"] >= 3, f"Unexpected: {res_chat}"
    assert "what do you mean if I check this for you?" in res_chat["corrected"], f"Got: {res_chat['corrected']}"
    print(f"[PASS] Windows OS ISpellChecker + Split-Word Fix ({res_chat['elapsed_ms']:.2f} ms): {res_chat['issue_count']} fixes -> {res_chat['corrected']}")

    # 2b. Test Finglish -> Persian Script & Persian Error Correction + Zero False-Positive English
    finglish_sample = "salam halet chetore"
    res_fing = nlp_engine.handle_request({"id": 13, "action": "analyze", "text": finglish_sample, "fast_badge_only": True})
    assert res_fing["ok"] and res_fing["issue_count"] >= 1, f"Unexpected Finglish response: {res_fing}"
    assert "سلام" in res_fing["corrected"] and "حالت" in res_fing["corrected"] and "چطوره" in res_fing["corrected"], f"Got: {res_fing['corrected']}"
    print(f"[PASS] Finglish -> Persian Script ({res_fing['elapsed_ms']:.2f} ms): '{finglish_sample}' -> '{res_fing['corrected']}' (pill: {res_fing['quick_summary']})")

    persian_err_sample = "صلام من می روم به خانه ها و سپاسگذارم از لطفا و اطاق بزرگ تر"
    res_fa = nlp_engine.handle_request({"id": 14, "action": "analyze", "text": persian_err_sample, "fast_badge_only": True})
    assert res_fa["ok"] and res_fa["issue_count"] >= 5, f"Unexpected Persian response: {res_fa}"
    assert "سلام" in res_fa["corrected"]
    assert "می\u200cروم" in res_fa["corrected"]
    assert "خانه\u200cها" in res_fa["corrected"]
    assert "سپاسگزارم" in res_fa["corrected"]
    assert "لطفاً" in res_fa["corrected"]
    assert "اتاق" in res_fa["corrected"]
    assert "بزرگ\u200cتر" in res_fa["corrected"]
    print(f"[PASS] Persian Spelling/Homophone/ZWNJ Fix ({res_fa['elapsed_ms']:.2f} ms): {res_fa['issue_count']} fixes -> '{res_fa['corrected']}'")

    # Ensure English sentences with overlapping words ('man', 'to', 'be', 'in', 'bad') stay English!
    ambig_en = "the man went to be in a bad room"
    res_ambig = nlp_engine.handle_request({"id": 15, "action": "analyze", "text": ambig_en, "fast_badge_only": True})
    assert nlp_engine.detect_language_mode(ambig_en) == "en", "English falsely detected as Finglish!"
    assert not nlp_engine.PERSIAN_SCRIPT_RE.search(res_ambig["corrected"])
    # Ensure Persian declarative word 'چراغ' does NOT trigger false question mark '؟'
    res_lamp = nlp_engine.handle_request({"id": 16, "action": "analyze", "text": "من چراغ اطاق را روشن کردم", "fast_badge_only": True})
    assert "اتاق" in res_lamp["corrected"] and not res_lamp["corrected"].endswith("؟"), f"Got: {res_lamp['corrected']}"
    print(f"[PASS] Language Disambiguation & Persian Reword/Format checks verified")

    fa_rw = nlp_engine.handle_request({"id": 17, "action": "reword", "mode": "formal", "text": "salam halet chetore", "use_llm": False})
    assert fa_rw["ok"] and "شما" in fa_rw["result"], f"Got: {fa_rw}"
    fa_em = nlp_engine.handle_request({"id": 18, "action": "format", "mode": "email_format", "text": "صلام گذارش پروژه آماده است", "use_llm": False})
    assert fa_em["ok"] and "با سلام و احترام" in fa_em["result"] and "گزارش" in fa_em["result"], f"Got: {fa_em}"

    # 2c. Test Dictionary RPC schema (Persian translation, synonyms, antonyms, examples)
    dict_off = nlp_engine.handle_request({"id": 19, "action": "dictionary", "text": "resilient", "use_llm": False})
    assert dict_off["ok"] and dict_off["persian_translation"] and len(dict_off["synonyms"]) >= 2 and len(dict_off["antonyms"]) >= 2 and len(dict_off["examples"]) >= 1
    print(f"[PASS] Dictionary RPC Schema ({dict_off['elapsed_ms']:.2f} ms): 'resilient' -> {dict_off['persian_translation']}")

    cfg_res = nlp_engine.handle_request({
        "id": 11,
        "action": "configure",
        "google_api_key": "",
        "google_model": "gemini-3.1-flash-lite-preview",
        "deepseek_api_key": "",
        "deepseek_model": "deepseek-chat",
        "deepgram_api_key": "",
        "deepgram_language": "multi",
        "ai_provider": "ai_only",
    })
    assert cfg_res["ok"] and cfg_res["google_model"] == "gemini-3.1-flash-lite-preview" and cfg_res["deepseek_model"] == "deepseek-chat"
    ai_only_res = nlp_engine.handle_request({"id": 110, "action": "analyze", "text": "i has went to teh store", "use_llm": True})
    assert ai_only_res["ok"] and "AI Only" in ai_only_res["engine"] and ai_only_res["corrected"] == "i has went to teh store"
    # Restore auto mode for offline test loop below
    nlp_engine.handle_request({"id": 111, "action": "configure", "ai_provider": "auto"})
    print("[PASS] Google Gemini + DeepSeek + Deepgram + 'AI Only' strict mode verified")

    # 2d. Test FERDOWSI v2 Voice Cleanup (strict non-responding transcript cleanup) vs Exact Voice + Read Aloud + Groq config
    v2_cfg = nlp_engine.handle_request({
        "id": 112,
        "action": "configure",
        "groq_api_key": "",
        "groq_model": "whisper-large-v3-turbo",
        "stt_provider": "auto",
        "voice_mode": "cleanup",
        "voice_ai_api_key": "",
        "tts_provider": "windows_sapi",
    })
    assert v2_cfg["ok"] and v2_cfg["groq_model"] == "whisper-large-v3-turbo" and v2_cfg["voice_mode"] == "cleanup"

    # Spoken question with fillers must ONLY be cleaned into the question itself, NEVER answered!
    spoken_question = "um uh what time is the meeting tomorrow"
    clean_q = nlp_engine.handle_request({
        "id": 113,
        "action": "clean_voice_text",
        "text": spoken_question,
        "voice_mode": "cleanup",
        "use_llm": False,
    })
    assert clean_q["ok"] and "what time is the meeting tomorrow" in clean_q["text"].lower(), f"Got: {clean_q}"
    # Verify anti-chatbot guard rejects conversational assistant replies
    assert not nlp_engine._is_safe_cleaned_transcript(spoken_question, "Sure! I don't have access to your calendar, so I cannot check your meeting time.")
    assert not nlp_engine._is_safe_cleaned_transcript("can you write an email to john", "Certainly, here is an email you can send to John:")
    assert nlp_engine._is_safe_cleaned_transcript(spoken_question, "What time is the meeting tomorrow?")

    # Exact mode must keep spoken words verbatim
    exact_q = nlp_engine.handle_request({
        "id": 114,
        "action": "clean_voice_text",
        "text": spoken_question,
        "voice_mode": "exact",
    })
    assert exact_q["ok"] and exact_q["text"] == spoken_question and exact_q["voice_mode"] == "exact"
    print(f"[PASS] FERDOWSI v2 Voice Modes verified: Cleanup='{clean_q['text']}' (non-responding guard active) | Exact='{exact_q['text']}'")

    for tone in ("fluent", "formal", "casual", "concise", "expand", "persuasive"):
        rw = nlp_engine.handle_request({
            "id": 2,
            "action": "reword",
            "mode": tone,
            "text": "hey can u send me teh report right now cuz i really need to check a lot of stuff in order to fix the problem",
        })
        assert rw["ok"] and rw["result"] and "teh" not in rw["result"].lower()
        print(f"[PASS] Reword [{tone:10s}] ({rw['elapsed_ms']:.2f} ms): {rw['result']}")

    for fmt in ("sentence_case", "title_case", "upper_case", "lower_case", "bullet_list", "numbered_list", "email_format", "snake_case", "camel_case"):
        fm = nlp_engine.handle_request({
            "id": 3,
            "action": "format",
            "mode": fmt,
            "text": "build the c++ dll, connect the python engine, and run the c# exe",
        })
        assert fm["ok"] and fm["result"]
    py_ms = (time.perf_counter() - t0) * 1000.0
    print(f"[PASS] All 9 Formatting modes verified ({py_ms:.2f} ms total)")

    # 3. Test Compiled 64-bit Standalone FERDOWSI.exe (--self-test)
    report_file = os.path.join(tempfile.gettempdir(), "ferdowsi_selftest_report.txt")
    if os.path.exists(report_file):
        os.remove(report_file)

    t0 = time.perf_counter()
    proc = subprocess.run([exe_path, "--self-test", report_file], capture_output=True, text=True, timeout=50)
    exe_ms = (time.perf_counter() - t0) * 1000.0
    assert os.path.isfile(report_file), "FERDOWSI.exe did not produce self-test report"
    report = open(report_file, "r", encoding="utf-8-sig").read()
    print("\n--- FERDOWSI.exe --self-test Report ---")
    print(report.strip())
    assert proc.returncode == 0 and "FAILURES: 0" in report, f"FERDOWSI.exe self-test failed (rc={proc.returncode})"

    total_ms = (time.perf_counter() - t_start) * 1000.0
    print(f"\n=== ALL TESTS PASSED in {total_ms:.1f} ms (EXE self-test: {exe_ms:.1f} ms) ===")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
