#define GRAMMERV_EXPORTS
#define UNICODE
#define _UNICODE
#define NOMINMAX

#include "grammerv_native.h"
#include <mmsystem.h>
#include <oleacc.h>
#include <oleauto.h>
#include <string>
#include <vector>
#include <algorithm>
#include <cwctype>
#include <cmath>
#include <cstring>

namespace {

CRITICAL_SECTION g_cs;
bool g_csInitialized = false;

HANDLE g_hookThread = NULL;
DWORD  g_hookThreadId = 0;
HANDLE g_readyEvent = NULL;
HHOOK  g_kbHook = NULL;
HHOOK  g_mouseHook = NULL;
HCURSOR g_hCursorIBeam = NULL;

HWND g_overlayHwnd = NULL;
HWND g_studioHwnd  = NULL;
HWND g_trackedHwnd = NULL;
bool g_isPasswordFocused = false;

std::wstring g_typedBuffer;
uint64_t g_lastTypeTick = 0;
uint64_t g_lastSelectTick = 0;
uint64_t g_lastIBeamClickTick = 0;
bool g_isTypingActive = false;
bool g_hasSelectionEvent = false;

bool g_hasIBeamAnchor = false;
int32_t g_ibeamAnchorX = 0;
int32_t g_ibeamAnchorY = 0;
int32_t g_ibeamLineChars = 0;

int32_t g_selectAnchorX = 0;
int32_t g_selectAnchorY = 0;

bool g_isMouseDown = false;
bool g_mouseDownIBeam = false;
POINT g_mouseDownPt = {0, 0};
uint64_t g_mouseDownTick = 0;
POINT g_lastClickUpPt = {0, 0};
uint64_t g_lastClickUpTick = 0;

bool IsPointInOverlay(POINT pt) {
    HWND ov = g_overlayHwnd;
    if (!ov || !IsWindow(ov) || !IsWindowVisible(ov)) return false;
    RECT rc;
    if (!GetWindowRect(ov, &rc)) return false;
    return PtInRect(&rc, pt) != FALSE;
}

bool CheckIsIBeamCursor() {
    CURSORINFO ci;
    ci.cbSize = sizeof(ci);
    if (!GetCursorInfo(&ci)) return false;
    if (!g_hCursorIBeam) {
        g_hCursorIBeam = LoadCursorW(NULL, IDC_IBEAM);
    }
    return (ci.flags == CURSOR_SHOWING) && (ci.hCursor == g_hCursorIBeam);
}

bool IsWin32PasswordField(HWND hwndFocus) {
    if (!hwndFocus || !IsWindow(hwndFocus)) return false;
    LONG style = GetWindowLongW(hwndFocus, GWL_STYLE);
    if ((style & ES_PASSWORD) != 0) return true;
    DWORD_PTR pwdChar = 0;
    if (SendMessageTimeoutW(hwndFocus, EM_GETPASSWORDCHAR, 0, 0, SMTO_ABORTIFHUNG, 10, &pwdChar) && pwdChar != 0) {
        return true;
    }
    return false;
}

LRESULT CALLBACK LowLevelKeyboardProc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode == HC_ACTION && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)) {
        const KBDLLHOOKSTRUCT* kb = reinterpret_cast<const KBDLLHOOKSTRUCT*>(lParam);
        if (!(kb->flags & LLKHF_INJECTED)) {
            HWND fg = GetForegroundWindow();
            if (fg && fg != g_overlayHwnd) {
                DWORD fgThread = GetWindowThreadProcessId(fg, NULL);
                GUITHREADINFO gti = {};
                gti.cbSize = sizeof(gti);
                HWND focusCtrl = fg;
                if (GetGUIThreadInfo(fgThread, &gti) && gti.hwndFocus) {
                    focusCtrl = gti.hwndFocus;
                }
                bool isWin32Pwd = IsWin32PasswordField(focusCtrl);

                EnterCriticalSection(&g_cs);
                if (g_isPasswordFocused || isWin32Pwd) {
                    g_typedBuffer.clear();
                    g_ibeamLineChars = 0;
                    g_isTypingActive = false;
                    LeaveCriticalSection(&g_cs);
                    return CallNextHookEx(NULL, nCode, wParam, lParam);
                }

                bool ctrlDown  = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool altDown   = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool winDown   = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

                if (fg != g_trackedHwnd) {
                    g_trackedHwnd = fg;
                    g_typedBuffer.clear();
                    g_ibeamLineChars = 0;
                }

                DWORD vk = kb->vkCode;
                uint64_t now = GetTickCount64();

                if (vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT ||
                    vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL ||
                    vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU ||
                    vk == VK_CAPITAL) {
                    // Modifier press alone: keep current buffer
                } else if (ctrlDown && shiftDown && !altDown && vk == '2') {
                    // Standard Windows Persian ZWNJ (نیم‌فاصله) shortcut: Ctrl+Shift+2
                    g_typedBuffer.push_back(static_cast<wchar_t>(0x200C));
                    g_ibeamLineChars++;
                    g_lastTypeTick = now;
                    g_isTypingActive = true;
                } else if (ctrlDown && !altDown) {
                    if (vk == 'A' || vk == 'X' || vk == 'V' || vk == 'Z' || vk == 'Y') {
                        if (vk == 'A') {
                            g_hasSelectionEvent = true;
                            g_lastSelectTick = now;
                        } else {
                            g_typedBuffer.clear();
                            g_ibeamLineChars = 0;
                        }
                    }
                } else if (shiftDown && (vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP ||
                                         vk == VK_DOWN || vk == VK_HOME || vk == VK_END)) {
                    g_hasSelectionEvent = true;
                    g_lastSelectTick = now;
                } else if (vk == VK_BACK) {
                    if (!g_typedBuffer.empty()) {
                        g_typedBuffer.pop_back();
                    }
                    if (g_ibeamLineChars > 0) g_ibeamLineChars--;
                    g_lastTypeTick = now;
                    g_isTypingActive = true;
                } else if (vk == VK_RETURN) {
                    g_typedBuffer.clear();
                    g_ibeamLineChars = 0;
                    if (g_hasIBeamAnchor) g_ibeamAnchorY += 18;
                    g_lastTypeTick = now;
                    g_isTypingActive = true;
                } else if (vk == VK_ESCAPE || vk == VK_LEFT || vk == VK_RIGHT ||
                           vk == VK_UP || vk == VK_DOWN || vk == VK_HOME || vk == VK_END ||
                           vk == VK_PRIOR || vk == VK_NEXT || vk == VK_DELETE || vk == VK_TAB) {
                    g_typedBuffer.clear();
                } else if (!ctrlDown && !altDown && !winDown) {
                    BYTE keyState[256];
                    memset(keyState, 0, sizeof(keyState));
                    if (shiftDown) {
                        keyState[VK_SHIFT] = 0x80;
                        keyState[VK_LSHIFT] = 0x80;
                    }
                    if (GetKeyState(VK_CAPITAL) & 0x0001) {
                        keyState[VK_CAPITAL] = 0x01;
                    }
                    DWORD fgThread = GetWindowThreadProcessId(fg, NULL);
                    GUITHREADINFO gti = {};
                    gti.cbSize = sizeof(gti);
                    if (GetGUIThreadInfo(fgThread, &gti) && gti.hwndFocus) {
                        fgThread = GetWindowThreadProcessId(gti.hwndFocus, NULL);
                    }
                    HKL hkl = GetKeyboardLayout(fgThread);
                    wchar_t chars[8] = {0};
                    // Flag 0x04 prevents mutating dead-key state on Windows 10/11
                    int count = ToUnicodeEx(vk, kb->scanCode, keyState, chars, 6, 0x04, hkl);
                    if (count > 0) {
                        bool added = false;
                        for (int i = 0; i < count; ++i) {
                            if (chars[i] >= 32) {
                                g_typedBuffer.push_back(chars[i]);
                                g_ibeamLineChars++;
                                added = true;
                            }
                        }
                        if (g_typedBuffer.size() > 600) {
                            size_t cut = g_typedBuffer.find(L' ', 120);
                            if (cut != std::wstring::npos && cut + 1 < g_typedBuffer.size()) {
                                g_typedBuffer.erase(0, cut + 1);
                            } else {
                                g_typedBuffer.erase(0, 120);
                            }
                        }
                        if (added) {
                            g_lastTypeTick = now;
                            g_isTypingActive = true;
                        }
                    }
                }
                LeaveCriticalSection(&g_cs);
            }
        }
    }
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode == HC_ACTION && (wParam == WM_LBUTTONDOWN || wParam == WM_LBUTTONUP)) {
        const MSLLHOOKSTRUCT* ms = reinterpret_cast<const MSLLHOOKSTRUCT*>(lParam);
        POINT pt = ms->pt;
        if (!IsPointInOverlay(pt)) {
            uint64_t now = GetTickCount64();
            bool isIBeam = CheckIsIBeamCursor();

            EnterCriticalSection(&g_cs);
            if (wParam == WM_LBUTTONDOWN) {
                g_isMouseDown = true;
                g_mouseDownPt = pt;
                g_mouseDownTick = now;
                g_mouseDownIBeam = isIBeam;
                g_typedBuffer.clear();

                if (isIBeam) {
                    g_hasIBeamAnchor = true;
                    g_ibeamAnchorX = pt.x;
                    g_ibeamAnchorY = pt.y;
                    g_ibeamLineChars = 0;
                    g_lastIBeamClickTick = now;
                    HWND fg = GetForegroundWindow();
                    if (fg && fg != g_overlayHwnd) {
                        g_trackedHwnd = fg;
                    }
                } else {
                    g_isTypingActive = false;
                }
            } else if (wParam == WM_LBUTTONUP) {
                if (g_isMouseDown) {
                    g_isMouseDown = false;
                    int dx = std::abs(pt.x - g_mouseDownPt.x);
                    int dy = std::abs(pt.y - g_mouseDownPt.y);
                    bool isDrag = (dx > 14 || dy > 14);
                    bool isDoubleClick = (now - g_lastClickUpTick < 460) &&
                                         (std::abs(pt.x - g_lastClickUpPt.x) < 12) &&
                                         (std::abs(pt.y - g_lastClickUpPt.y) < 12);

                    if ((isDrag && (g_mouseDownIBeam || isIBeam)) ||
                        (isDoubleClick && (g_mouseDownIBeam || isIBeam))) {
                        g_hasSelectionEvent = true;
                        g_lastSelectTick = now;
                        g_selectAnchorX = pt.x;
                        g_selectAnchorY = std::min(pt.y, g_mouseDownPt.y);
                        HWND fg = GetForegroundWindow();
                        if (fg && fg != g_overlayHwnd) {
                            g_trackedHwnd = fg;
                        }
                    }
                    g_lastClickUpTick = now;
                    g_lastClickUpPt = pt;
                }
            }
            LeaveCriticalSection(&g_cs);
        }
    }
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

DWORD WINAPI HookThreadProc(LPVOID) {
    MSG msg;
    PeekMessageW(&msg, NULL, WM_USER, WM_USER, PM_NOREMOVE);
    g_hCursorIBeam = LoadCursorW(NULL, IDC_IBEAM);
    HINSTANCE hMod = GetModuleHandleW(NULL);
    g_kbHook = SetWindowsHookExW(WH_KEYBOARD_LL, LowLevelKeyboardProc, hMod, 0);
    g_mouseHook = SetWindowsHookExW(WH_MOUSE_LL, LowLevelMouseProc, hMod, 0);
    if (g_readyEvent) SetEvent(g_readyEvent);

    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    if (g_kbHook) { UnhookWindowsHookEx(g_kbHook); g_kbHook = NULL; }
    if (g_mouseHook) { UnhookWindowsHookEx(g_mouseHook); g_mouseHook = NULL; }
    return 0;
}

bool ReadClipboardUnicode(std::wstring& outText) {
    outText.clear();
    bool hasText = false;
    for (int retry = 0; retry < 6; ++retry) {
        if (OpenClipboard(NULL)) {
            if (IsClipboardFormatAvailable(CF_UNICODETEXT)) {
                HANDLE hData = GetClipboardData(CF_UNICODETEXT);
                if (hData) {
                    const wchar_t* psz = static_cast<const wchar_t*>(GlobalLock(hData));
                    if (psz) {
                        outText.assign(psz);
                        hasText = true;
                        GlobalUnlock(hData);
                    }
                }
            }
            CloseClipboard();
            break;
        }
        Sleep(12);
    }
    return hasText;
}

bool WriteClipboardUnicode(const std::wstring& text, bool hadOriginalText) {
    for (int retry = 0; retry < 6; ++retry) {
        if (OpenClipboard(NULL)) {
            EmptyClipboard();
            if (hadOriginalText && !text.empty()) {
                size_t bytes = (text.size() + 1) * sizeof(wchar_t);
                HGLOBAL hMem = GlobalAlloc(GMEM_MOVEABLE, bytes);
                if (hMem) {
                    void* ptr = GlobalLock(hMem);
                    if (ptr) {
                        memcpy(ptr, text.c_str(), bytes);
                        GlobalUnlock(hMem);
                        SetClipboardData(CF_UNICODETEXT, hMem);
                    } else {
                        GlobalFree(hMem);
                    }
                }
            }
            CloseClipboard();
            return true;
        }
        Sleep(12);
    }
    return false;
}

void ReleaseModifiersIfHeld() {
    std::vector<INPUT> rel;
    auto addUp = [&](WORD vk) {
        if (GetAsyncKeyState(vk) & 0x8000) {
            INPUT in = {};
            in.type = INPUT_KEYBOARD;
            in.ki.wVk = vk;
            in.ki.dwFlags = KEYEVENTF_KEYUP;
            rel.push_back(in);
        }
    };
    addUp(VK_CONTROL);
    addUp(VK_LCONTROL);
    addUp(VK_RCONTROL);
    addUp(VK_MENU);
    addUp(VK_LMENU);
    addUp(VK_RMENU);
    addUp(VK_SHIFT);
    addUp(VK_LSHIFT);
    addUp(VK_RSHIFT);
    if (!rel.empty()) {
        SendInput(static_cast<UINT>(rel.size()), rel.data(), sizeof(INPUT));
        Sleep(12);
    }
}

void SendSingleVk(WORD vk) {
    INPUT inputs[2] = {};
    inputs[0].type = INPUT_KEYBOARD;
    inputs[0].ki.wVk = vk;
    inputs[1].type = INPUT_KEYBOARD;
    inputs[1].ki.wVk = vk;
    inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
    SendInput(2, inputs, sizeof(INPUT));
}

void SendShiftEnter() {
    INPUT inputs[4] = {};
    inputs[0].type = INPUT_KEYBOARD;
    inputs[0].ki.wVk = VK_SHIFT;

    inputs[1].type = INPUT_KEYBOARD;
    inputs[1].ki.wVk = VK_RETURN;

    inputs[2].type = INPUT_KEYBOARD;
    inputs[2].ki.wVk = VK_RETURN;
    inputs[2].ki.dwFlags = KEYEVENTF_KEYUP;

    inputs[3].type = INPUT_KEYBOARD;
    inputs[3].ki.wVk = VK_SHIFT;
    inputs[3].ki.dwFlags = KEYEVENTF_KEYUP;

    SendInput(4, inputs, sizeof(INPUT));
}

void SendCtrlKey(WORD letterVk) {
    ReleaseModifiersIfHeld();
    INPUT inputs[4] = {};
    inputs[0].type = INPUT_KEYBOARD;
    inputs[0].ki.wVk = VK_CONTROL;

    inputs[1].type = INPUT_KEYBOARD;
    inputs[1].ki.wVk = letterVk;

    inputs[2].type = INPUT_KEYBOARD;
    inputs[2].ki.wVk = letterVk;
    inputs[2].ki.dwFlags = KEYEVENTF_KEYUP;

    inputs[3].type = INPUT_KEYBOARD;
    inputs[3].ki.wVk = VK_CONTROL;
    inputs[3].ki.dwFlags = KEYEVENTF_KEYUP;

    SendInput(4, inputs, sizeof(INPUT));
}

// Direct Win32 KEYEVENTF_UNICODE streaming: works reliably in Edge, Chrome, React,
// Lexical, ProseMirror, search boxes, and native Windows controls without clipboard races!
void SendUnicodeTextDirect(const std::wstring& text) {
    ReleaseModifiersIfHeld();
    std::vector<INPUT> batch;
    batch.reserve(40);
    int charsInBatch = 0;

    auto flushBatch = [&]() {
        if (!batch.empty()) {
            SendInput(static_cast<UINT>(batch.size()), batch.data(), sizeof(INPUT));
            batch.clear();
            charsInBatch = 0;
            Sleep(4);
        }
    };

    for (size_t i = 0; i < text.size(); ++i) {
        wchar_t ch = text[i];
        if (ch == L'\r') {
            if (i + 1 < text.size() && text[i + 1] == L'\n') {
                continue; // Handled on '\n'
            }
            ch = L'\n';
        }

        if (ch == L'\n') {
            flushBatch();
            SendShiftEnter();
            Sleep(12);
            continue;
        }

        INPUT down = {};
        down.type = INPUT_KEYBOARD;
        down.ki.wVk = 0;
        down.ki.wScan = static_cast<WORD>(ch);
        down.ki.dwFlags = KEYEVENTF_UNICODE;
        batch.push_back(down);

        INPUT up = {};
        up.type = INPUT_KEYBOARD;
        up.ki.wVk = 0;
        up.ki.wScan = static_cast<WORD>(ch);
        up.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
        batch.push_back(up);
        charsInBatch++;

        // Keep UTF-16 high surrogate (0xD800..0xDBFF) paired with its low surrogate in the same SendInput call
        bool isHighSurrogate = (ch >= 0xD800 && ch <= 0xDBFF);
        if (charsInBatch >= 16 && !isHighSurrogate) {
            flushBatch();
        }
    }
    flushBatch();
}

struct TypoPair {
    const wchar_t* bad;
    const wchar_t* good;
};

static const TypoPair kDirectTypos[] = {
    {L"teh", L"the"}, {L"hte", L"the"}, {L"taht", L"that"}, {L"thta", L"that"},
    {L"wiht", L"with"}, {L"iwth", L"with"}, {L"waht", L"what"}, {L"hwat", L"what"},
    {L"wihch", L"which"}, {L"wich", L"which"}, {L"whcih", L"which"}, {L"wen", L"when"},
    {L"whn", L"when"}, {L"wehr", L"where"}, {L"wher", L"where"}, {L"thier", L"their"},
    {L"theri", L"their"}, {L"tihs", L"this"}, {L"htis", L"this"}, {L"thsi", L"this"},
    {L"yuor", L"your"}, {L"yoru", L"your"}, {L"yuo", L"you"}, {L"abuot", L"about"},
    {L"abotu", L"about"}, {L"baout", L"about"}, {L"form", L"form"}, {L"fomr", L"from"},
    {L"frmo", L"from"}, {L"ahve", L"have"}, {L"hvae", L"have"}, {L"haev", L"have"},
    {L"jsut", L"just"}, {L"jstu", L"just"}, {L"konw", L"know"}, {L"knwo", L"know"},
    {L"nkow", L"know"}, {L"liek", L"like"}, {L"lkie", L"like"}, {L"amke", L"make"},
    {L"mkae", L"make"}, {L"moer", L"more"}, {L"mroe", L"more"}, {L"onlu", L"only"},
    {L"olny", L"only"}, {L"otehr", L"other"}, {L"ohter", L"other"}, {L"peopel", L"people"},
    {L"poeple", L"people"}, {L"peolpe", L"people"}, {L"shoudl", L"should"}, {L"shuold", L"should"},
    {L"woudl", L"would"}, {L"wuold", L"would"}, {L"coudl", L"could"}, {L"cuold", L"could"},
    {L"thign", L"thing"}, {L"hting", L"thing"}, {L"tihnk", L"think"}, {L"thnik", L"think"},
    {L"tiem", L"time"}, {L"tmie", L"time"}, {L"vre", L"very"}, {L"vrey", L"very"},
    {L"veyr", L"very"}, {L"wnat", L"want"}, {L"watn", L"want"}, {L"wrok", L"work"},
    {L"owrk", L"work"}, {L"wrold", L"world"}, {L"wolrd", L"world"}, {L"yaer", L"year"},
    {L"yera", L"year"}, {L"alos", L"also"}, {L"aslo", L"also"}, {L"awya", L"away"},
    {L"abck", L"back"}, {L"bcak", L"back"}, {L"ebcause", L"because"}, {L"becuase", L"because"},
    {L"beacuse", L"because"}, {L"becasue", L"because"}, {L"cuz", L"because"},
    {L"grammer", L"grammar"}, {L"gramar", L"grammar"}, {L"cursur", L"cursor"},
    {L"curser", L"cursor"}, {L"requiered", L"required"}, {L"requried", L"required"},
    {L"requird", L"required"}, {L"recieve", L"receive"}, {L"recieved", L"received"},
    {L"beleive", L"believe"}, {L"beleived", L"believed"}, {L"acheive", L"achieve"},
    {L"wierd", L"weird"}, {L"freind", L"friend"}, {L"freinds", L"friends"},
    {L"definately", L"definitely"}, {L"definitly", L"definitely"}, {L"definetly", L"definitely"},
    {L"seperate", L"separate"}, {L"seperated", L"separated"}, {L"occured", L"occurred"},
    {L"occuring", L"occurring"}, {L"occassion", L"occasion"}, {L"untill", L"until"},
    {L"adress", L"address"}, {L"accomodate", L"accommodate"}, {L"arguement", L"argument"},
    {L"begining", L"beginning"}, {L"buisness", L"business"}, {L"calender", L"calendar"},
    {L"collegue", L"colleague"}, {L"comming", L"coming"}, {L"completly", L"completely"},
    {L"concious", L"conscious"}, {L"curiousity", L"curiosity"}, {L"disappear", L"disappear"},
    {L"dissapear", L"disappear"}, {L"embarass", L"embarrass"}, {L"enviroment", L"environment"},
    {L"existance", L"existence"}, {L"familar", L"familiar"}, {L"finaly", L"finally"},
    {L"foriegn", L"foreign"}, {L"fourty", L"forty"}, {L"foward", L"forward"},
    {L"goverment", L"government"}, {L"garantee", L"guarantee"}, {L"gaurantee", L"guarantee"},
    {L"happend", L"happened"}, {L"harass", L"harass"}, {L"harrass", L"harass"},
    {L"humourous", L"humorous"}, {L"immediatly", L"immediately"}, {L"independant", L"independent"},
    {L"interupt", L"interrupt"}, {L"knowlege", L"knowledge"}, {L"liason", L"liaison"},
    {L"maintenence", L"maintenance"}, {L"millenium", L"millennium"}, {L"neccessary", L"necessary"},
    {L"necesary", L"necessary"}, {L"noticable", L"noticeable"}, {L"parliment", L"parliament"},
    {L"persistant", L"persistent"}, {L"posession", L"possession"}, {L"prefered", L"preferred"},
    {L"priviledge", L"privilege"}, {L"proffesional", L"professional"}, {L"profesional", L"professional"},
    {L"publically", L"publicly"}, {L"questionaire", L"questionnaire"}, {L"realyl", L"really"},
    {L"realy", L"really"}, {L"recomend", L"recommend"}, {L"reccomend", L"recommend"},
    {L"refered", L"referred"}, {L"religous", L"religious"}, {L"remeber", L"remember"},
    {L"rember", L"remember"}, {L"resistence", L"resistance"}, {L"sence", L"sense"},
    {L"sentance", L"sentence"}, {L"sentances", L"sentences"}, {L"speling", L"spelling"},
    {L"succesful", L"successful"}, {L"successfull", L"successful"}, {L"suprise", L"surprise"},
    {L"surprize", L"surprise"}, {L"tendancy", L"tendency"}, {L"therefor", L"therefore"},
    {L"threshhold", L"threshold"}, {L"tommorow", L"tomorrow"}, {L"tomorow", L"tomorrow"},
    {L"tounge", L"tongue"}, {L"truely", L"truly"}, {L"unfortunatly", L"unfortunately"},
    {L"unforseen", L"unforeseen"}, {L"wether", L"whether"}, {L"writting", L"writing"},
    {L"aplication", L"application"}, {L"computr", L"computer"}, {L"langauge", L"language"},
    {L"mesage", L"message"}, {L"probelm", L"problem"}, {L"problme", L"problem"},
    {L"exampel", L"example"}, {L"awsome", L"awesome"}, {L"beter", L"better"},
    {L"grate", L"great"}, {L"pleaes", L"please"}, {L"plase", L"please"},
    {L"thansk", L"thanks"}, {L"thnaks", L"thanks"}, {L"welocme", L"welcome"},
    {L"yestarday", L"yesterday"}, {L"yesterady", L"yesterday"}
};

// High-frequency core lexicon (5+ chars) for native C++ -O3 Damerau-Levenshtein spell correction
static const wchar_t* kCoreLexicon[] = {
    L"about", L"above", L"accept", L"access", L"account", L"accurate", L"achieve", L"across",
    L"action", L"active", L"actually", L"address", L"addition", L"advance", L"advice", L"advise",
    L"affect", L"after", L"again", L"against", L"agree", L"almost", L"alone", L"along",
    L"already", L"also", L"although", L"always", L"amazing", L"among", L"amount", L"analysis",
    L"analyze", L"another", L"answer", L"anything", L"anywhere", L"appear", L"apple", L"apples",
    L"application", L"apply", L"approach", L"appropriate", L"argument", L"around", L"arrange", L"arrive",
    L"article", L"assistant", L"available", L"average", L"avoid", L"awesome", L"balance", L"basically",
    L"beautiful", L"because", L"become", L"before", L"begin", L"beginning", L"behavior", L"behind",
    L"believe", L"benefit", L"better", L"between", L"beyond", L"brother", L"brown", L"build",
    L"building", L"business", L"button", L"calendar", L"camera", L"campaign", L"cannot", L"capital",
    L"career", L"careful", L"carefully", L"category", L"center", L"certain", L"certainly", L"challenge",
    L"chance", L"change", L"changing", L"character", L"check", L"choice", L"choose", L"clear",
    L"clearly", L"client", L"close", L"college", L"color", L"column", L"combine", L"coming",
    L"command", L"comment", L"committee", L"common", L"community", L"company", L"compare", L"complete",
    L"completely", L"complex", L"computer", L"concern", L"condition", L"conference", L"confident", L"confirm",
    L"connect", L"consider", L"consistent", L"contact", L"contain", L"content", L"continue", L"control",
    L"convenient", L"conversation", L"correct", L"correction", L"correctly", L"could", L"country", L"couple",
    L"course", L"create", L"creative", L"critical", L"culture", L"current", L"currently", L"cursor",
    L"customer", L"daily", L"damage", L"data", L"database", L"daughter", L"decide", L"decision",
    L"default", L"definitely", L"definition", L"degree", L"deliver", L"delivery", L"department", L"depend",
    L"describe", L"description", L"design", L"desktop", L"despite", L"detail", L"detailed", L"determine",
    L"develop", L"development", L"device", L"difference", L"different", L"difficult", L"direct", L"directly",
    L"direction", L"director", L"disappear", L"discover", L"discuss", L"discussion", L"distance", L"document",
    L"documentation", L"during", L"early", L"easily", L"education", L"effect", L"effective", L"effort",
    L"either", L"element", L"email", L"embarrass", L"employee", L"enable", L"encourage", L"energy",
    L"engine", L"engineer", L"engineering", L"english", L"enjoy", L"enough", L"ensure", L"enter",
    L"entire", L"environment", L"equipment", L"error", L"especially", L"essential", L"establish", L"evaluate",
    L"evening", L"event", L"eventually", L"every", L"everybody", L"everyone", L"everything", L"everywhere",
    L"evidence", L"exact", L"exactly", L"example", L"excellent", L"except", L"excite", L"excited",
    L"exciting", L"exercise", L"exist", L"existence", L"expand", L"expect", L"experience", L"experiment",
    L"expert", L"explain", L"explanation", L"explore", L"express", L"extra", L"extremely", L"family",
    L"familiar", L"famous", L"favorite", L"feature", L"February", L"feedback", L"feeling", L"field",
    L"figure", L"final", L"finally", L"financial", L"finish", L"first", L"fluent", L"focus",
    L"follow", L"following", L"foreign", L"forget", L"formal", L"format", L"formatting", L"forward",
    L"friend", L"friendly", L"function", L"further", L"future", L"general", L"generally", L"generate",
    L"global", L"government", L"grammar", L"grateful", L"great", L"group", L"growth", L"guarantee",
    L"guidance", L"handle", L"happen", L"happened", L"happening", L"happy", L"health", L"healthy",
    L"helpful", L"helping", L"history", L"honest", L"hopefully", L"hospital", L"however", L"human",
    L"hundred", L"identify", L"ignore", L"image", L"imagine", L"immediate", L"immediately", L"impact",
    L"implement", L"important", L"improve", L"improvement", L"include", L"including", L"increase", L"indeed",
    L"independent", L"indicate", L"individual", L"industry", L"information", L"initial", L"input", L"inside",
    L"install", L"instance", L"instead", L"instruction", L"integrate", L"intelligent", L"interest", L"interested",
    L"interesting", L"interface", L"internal", L"international", L"internet", L"interview", L"introduce", L"issue",
    L"itself", L"journey", L"judgment", L"keyboard", L"knowledge", L"language", L"large", L"later",
    L"launch", L"leader", L"learn", L"learning", L"least", L"leave", L"length", L"letter",
    L"level", L"library", L"license", L"likely", L"limit", L"listen", L"little", L"local",
    L"location", L"looking", L"machine", L"maintain", L"maintenance", L"major", L"manage", L"management",
    L"manager", L"manner", L"manual", L"market", L"material", L"matter", L"maximum", L"maybe",
    L"meaning", L"measure", L"media", L"medical", L"medium", L"meeting", L"member", L"memory",
    L"mention", L"message", L"method", L"middle", L"might", L"million", L"minimum", L"minute",
    L"mistake", L"model", L"modern", L"moment", L"Monday", L"money", L"monitor", L"month",
    L"morning", L"mostly", L"mother", L"mountain", L"mouse", L"movement", L"multiple", L"music",
    L"myself", L"natural", L"naturally", L"nature", L"nearly", L"necessary", L"network", L"never",
    L"normal", L"normally", L"nothing", L"notice", L"number", L"numerous", L"object", L"observe",
    L"obtain", L"obvious", L"obviously", L"occasion", L"occur", L"occurred", L"offer", L"office",
    L"official", L"often", L"online", L"operate", L"operation", L"opinion", L"opportunity", L"option",
    L"order", L"ordinary", L"organization", L"organize", L"original", L"other", L"otherwise", L"outside",
    L"overall", L"package", L"paragraph", L"parent", L"particular", L"particularly", L"partner", L"patient",
    L"pattern", L"payment", L"people", L"percent", L"perfect", L"perform", L"performance", L"perhaps",
    L"period", L"permanent", L"permission", L"person", L"personal", L"persuasive", L"phone", L"phrase",
    L"physical", L"picture", L"piece", L"place", L"planning", L"platform", L"please", L"pleasure",
    L"point", L"policy", L"popular", L"position", L"positive", L"possible", L"possibly", L"potential",
    L"power", L"powerful", L"practical", L"practice", L"prefer", L"prepare", L"presence", L"present",
    L"president", L"pressure", L"pretty", L"prevent", L"previous", L"previously", L"price", L"primary",
    L"private", L"probably", L"problem", L"procedure", L"process", L"produce", L"product", L"production",
    L"professional", L"profile", L"program", L"programming", L"progress", L"project", L"promise", L"promote",
    L"proper", L"properly", L"property", L"protect", L"provide", L"public", L"publish", L"purchase",
    L"purpose", L"Python", L"quality", L"quarter", L"question", L"quick", L"quickly", L"quiet",
    L"quite", L"raise", L"range", L"rather", L"reach", L"reaction", L"reading", L"ready",
    L"realize", L"really", L"reason", L"reasonable", L"receive", L"received", L"recent", L"recently",
    L"recognize", L"recommend", L"recommendation", L"record", L"reduce", L"refer", L"reference", L"reflect",
    L"regard", L"regarding", L"region", L"regular", L"relate", L"relation", L"relationship", L"relative",
    L"release", L"relevant", L"reliable", L"remain", L"remember", L"remove", L"repair", L"repeat",
    L"replace", L"reply", L"report", L"represent", L"request", L"require", L"required", L"requirement",
    L"research", L"resolve", L"resource", L"respect", L"respond", L"response", L"responsibility", L"responsible",
    L"restaurant", L"result", L"return", L"reveal", L"review", L"reword", L"rewrite", L"right",
    L"running", L"safety", L"sample", L"schedule", L"school", L"science", L"scientific", L"screen",
    L"search", L"season", L"second", L"secret", L"section", L"secure", L"security", L"select",
    L"selection", L"senior", L"sense", L"sentence", L"sentences", L"separate", L"series", L"serious",
    L"seriously", L"service", L"session", L"setting", L"several", L"share", L"short", L"should",
    L"shoulder", L"show", L"shown", L"signal", L"significant", L"similar", L"simple", L"simply",
    L"since", L"single", L"sister", L"situation", L"skill", L"small", L"smart", L"smooth",
    L"social", L"software", L"solution", L"solve", L"someone", L"something", L"sometimes", L"somewhat",
    L"somewhere", L"sorry", L"sound", L"source", L"space", L"speak", L"special", L"specific",
    L"specifically", L"speech", L"speed", L"spell", L"spelling", L"spend", L"standard", L"start",
    L"state", L"statement", L"station", L"status", L"still", L"store", L"story", L"straight",
    L"strange", L"strategy", L"street", L"strength", L"strong", L"structure", L"student", L"studio",
    L"study", L"style", L"subject", L"submit", L"success", L"successful", L"successfully", L"suddenly",
    L"suffer", L"suggest", L"suggestion", L"summary", L"summer", L"support", L"suppose", L"surface",
    L"surprise", L"Switch", L"symbol", L"system", L"table", L"target", L"teacher", L"technical",
    L"technique", L"technology", L"television", L"temperature", L"temporary", L"testing", L"thank", L"thanks",
    L"their", L"themselves", L"theory", L"there", L"therefore", L"these", L"thing", L"things",
    L"think", L"thinking", L"third", L"though", L"thought", L"thousand", L"thread", L"three",
    L"through", L"throughout", L"title", L"today", L"together", L"tomorrow", L"tonight", L"topic",
    L"total", L"touch", L"toward", L"towards", L"track", L"traditional", L"training", L"transfer",
    L"transform", L"translate", L"travel", L"treat", L"treatment", L"trouble", L"truly", L"trust",
    L"truth", L"trying", L"typing", L"typical", L"understand", L"understanding", L"unfortunately", L"unique",
    L"united", L"university", L"unless", L"unlikely", L"until", L"update", L"useful", L"usually",
    L"value", L"various", L"version", L"video", L"visit", L"visual", L"voice", L"volume",
    L"waiting", L"walking", L"watch", L"water", L"weather", L"website", L"Wednesday", L"weekend",
    L"weight", L"weird", L"welcome", L"whatever", L"whenever", L"where", L"whether", L"which",
    L"while", L"whole", L"whose", L"window", L"windows", L"within", L"without", L"wonder",
    L"wonderful", L"words", L"working", L"world", L"worry", L"would", L"write", L"writing",
    L"written", L"wrong", L"yesterday", L"young", L"yourself"
};

int ComputeDamerau(const std::wstring& s1, const std::wstring& s2) {
    const size_t len1 = s1.size();
    const size_t len2 = s2.size();
    if (len1 == 0) return static_cast<int>(len2);
    if (len2 == 0) return static_cast<int>(len1);
    if (len1 > 64 || len2 > 64) {
        return static_cast<int>(std::abs(static_cast<int>(len1) - static_cast<int>(len2)) + 10);
    }

    int dp[66][66];
    for (size_t i = 0; i <= len1; ++i) dp[i][0] = static_cast<int>(i);
    for (size_t j = 0; j <= len2; ++j) dp[0][j] = static_cast<int>(j);

    for (size_t i = 1; i <= len1; ++i) {
        for (size_t j = 1; j <= len2; ++j) {
            int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
            int del = dp[i - 1][j] + 1;
            int ins = dp[i][j - 1] + 1;
            int sub = dp[i - 1][j - 1] + cost;
            int best = std::min({del, ins, sub});
            if (i > 1 && j > 1 && s1[i - 1] == s2[j - 2] && s1[i - 2] == s2[j - 1]) {
                best = std::min(best, dp[i - 2][j - 2] + 1);
            }
            dp[i][j] = best;
        }
    }
    return dp[len1][len2];
}

} // namespace

extern "C" {

GV_API int32_t gv_init(void) {
    if (!g_csInitialized) {
        InitializeCriticalSection(&g_cs);
        g_csInitialized = true;
    }
    if (g_hookThread != NULL) return 1;

    g_readyEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    g_hookThread = CreateThread(NULL, 0, HookThreadProc, NULL, 0, &g_hookThreadId);
    if (!g_hookThread) {
        if (g_readyEvent) { CloseHandle(g_readyEvent); g_readyEvent = NULL; }
        return 0;
    }
    if (g_readyEvent) {
        WaitForSingleObject(g_readyEvent, 1500);
        CloseHandle(g_readyEvent);
        g_readyEvent = NULL;
    }
    return 1;
}

GV_API void gv_shutdown(void) {
    if (g_hookThread && g_hookThreadId != 0) {
        PostThreadMessageW(g_hookThreadId, WM_QUIT, 0, 0);
        WaitForSingleObject(g_hookThread, 1500);
        CloseHandle(g_hookThread);
        g_hookThread = NULL;
        g_hookThreadId = 0;
    }
}

GV_API void gv_set_own_windows(int64_t overlay_hwnd, int64_t studio_hwnd) {
    if (!g_csInitialized) return;
    EnterCriticalSection(&g_cs);
    g_overlayHwnd = reinterpret_cast<HWND>(static_cast<intptr_t>(overlay_hwnd));
    g_studioHwnd  = reinterpret_cast<HWND>(static_cast<intptr_t>(studio_hwnd));
    LeaveCriticalSection(&g_cs);
}

GV_API void gv_set_password_mode(int32_t is_password) {
    if (!g_csInitialized) return;
    EnterCriticalSection(&g_cs);
    g_isPasswordFocused = (is_password != 0);
    if (g_isPasswordFocused) {
        g_typedBuffer.clear();
        g_ibeamLineChars = 0;
        g_isTypingActive = false;
        g_hasSelectionEvent = false;
    }
    LeaveCriticalSection(&g_cs);
}

GV_API int32_t gv_poll_state(GvMonitorState* out_state) {
    if (!out_state) return 0;
    memset(out_state, 0, sizeof(GvMonitorState));

    POINT mousePt = {0, 0};
    GetCursorPos(&mousePt);
    out_state->mouse_x = mousePt.x;
    out_state->mouse_y = mousePt.y;
    out_state->is_ibeam_cursor = CheckIsIBeamCursor() ? 1 : 0;

    HWND fg = GetForegroundWindow();
    EnterCriticalSection(&g_cs);
    if (!fg || fg == g_overlayHwnd) {
        if (g_trackedHwnd && IsWindow(g_trackedHwnd)) {
            fg = g_trackedHwnd;
        }
    } else {
        g_trackedHwnd = fg;
    }

    uint64_t now = GetTickCount64();
    if (g_isTypingActive && (now - g_lastTypeTick > 14000)) {
        g_isTypingActive = false;
    }

    out_state->has_selection_event = g_hasSelectionEvent ? 1 : 0;
    out_state->is_typing_active    = g_isTypingActive ? 1 : 0;
    out_state->typed_char_count    = static_cast<int32_t>(g_typedBuffer.size());
    out_state->last_type_tick      = g_lastTypeTick;
    out_state->last_select_tick    = g_lastSelectTick;
    out_state->foreground_hwnd     = static_cast<int64_t>(reinterpret_cast<intptr_t>(fg));
    out_state->is_password_focused = g_isPasswordFocused ? 1 : 0;

    bool hasSelectAnchor = (now - g_lastSelectTick < 15000) && (g_selectAnchorX != 0 || g_selectAnchorY != 0);
    bool hasIBeam = g_hasIBeamAnchor && (now - std::max(g_lastIBeamClickTick, g_lastTypeTick) < 20000);
    int32_t selX = g_selectAnchorX;
    int32_t selY = g_selectAnchorY;
    int32_t ibX  = g_ibeamAnchorX + std::min(g_ibeamLineChars * 7, 240);
    int32_t ibY  = g_ibeamAnchorY;
    LeaveCriticalSection(&g_cs);

    if (!fg || !IsWindow(fg)) return 1;

    DWORD fgThread = GetWindowThreadProcessId(fg, NULL);
    GUITHREADINFO gti;
    memset(&gti, 0, sizeof(gti));
    gti.cbSize = sizeof(gti);
    HWND focusHwnd = fg;

    if (GetGUIThreadInfo(fgThread, &gti)) {
        if (gti.hwndFocus) focusHwnd = gti.hwndFocus;
        out_state->focus_hwnd = static_cast<int64_t>(reinterpret_cast<intptr_t>(focusHwnd));

        if (IsWin32PasswordField(focusHwnd)) {
            EnterCriticalSection(&g_cs);
            g_typedBuffer.clear();
            g_ibeamLineChars = 0;
            g_isTypingActive = false;
            LeaveCriticalSection(&g_cs);
            out_state->is_password_focused = 1;
            out_state->is_typing_active = 0;
            out_state->typed_char_count = 0;
            return 1;
        }

        if (gti.hwndCaret && (gti.rcCaret.bottom > gti.rcCaret.top || gti.rcCaret.right > gti.rcCaret.left)) {
            RECT rcCaretClient = {0, 0, 0, 0};
            GetClientRect(gti.hwndCaret, &rcCaretClient);
            bool isBogusOriginCaret = (gti.rcCaret.left <= 2 && gti.rcCaret.top <= 2) &&
                                      ((rcCaretClient.right - rcCaretClient.left) > 500 &&
                                       (rcCaretClient.bottom - rcCaretClient.top) > 220);
            if (!isBogusOriginCaret) {
                POINT tl = { gti.rcCaret.left, gti.rcCaret.top };
                POINT br = { gti.rcCaret.right, gti.rcCaret.bottom };
                if (ClientToScreen(gti.hwndCaret, &tl) && ClientToScreen(gti.hwndCaret, &br)) {
                    if ((tl.x != 0 || tl.y != 0) && tl.x > -16000 && tl.y > -16000 && (br.y - tl.y) <= 120) {
                        out_state->caret_x = tl.x;
                        out_state->caret_y = tl.y;
                        out_state->caret_w = std::max<int32_t>(1, br.x - tl.x);
                        out_state->caret_h = std::max<int32_t>(16, br.y - tl.y);
                        out_state->caret_source = 1;
                        return 1;
                    }
                }
            }
        }
    } else {
        out_state->focus_hwnd = static_cast<int64_t>(reinterpret_cast<intptr_t>(focusHwnd));
    }

    // Strategy 2: MSAA OBJID_CARET
    IAccessible* pAcc = NULL;
    HRESULT hr = AccessibleObjectFromWindow(focusHwnd, OBJID_CARET, IID_IAccessible, reinterpret_cast<void**>(&pAcc));
    if (FAILED(hr) && focusHwnd != fg) {
        hr = AccessibleObjectFromWindow(fg, OBJID_CARET, IID_IAccessible, reinterpret_cast<void**>(&pAcc));
    }
    if (SUCCEEDED(hr) && pAcc) {
        VARIANT varSelf;
        VariantInit(&varSelf);
        varSelf.vt = VT_I4;
        varSelf.lVal = CHILDID_SELF;
        long ax = 0, ay = 0, aw = 0, ah = 0;
        if (SUCCEEDED(pAcc->accLocation(&ax, &ay, &aw, &ah, varSelf))) {
            RECT rcFocus = {0, 0, 0, 0};
            GetWindowRect(focusHwnd, &rcFocus);
            bool isBogusTopLeft = ((rcFocus.right - rcFocus.left) > 500 && (rcFocus.bottom - rcFocus.top) > 220) &&
                                  (std::abs(ax - rcFocus.left) <= 16 && std::abs(ay - rcFocus.top) <= 16);
            if (!isBogusTopLeft && (ax != 0 || ay != 0) && (ah > 0 || aw > 0) && ah <= 120 && aw <= 400 && ax > -16000 && ay > -16000) {
                out_state->caret_x = static_cast<int32_t>(ax);
                out_state->caret_y = static_cast<int32_t>(ay);
                out_state->caret_w = std::max<int32_t>(1, static_cast<int32_t>(aw));
                out_state->caret_h = std::max<int32_t>(16, static_cast<int32_t>(ah));
                out_state->caret_source = 2;
                pAcc->Release();
                return 1;
            }
        }
        pAcc->Release();
    }

    // Strategy 4: Native Selection or I-Beam Click/Typing Anchor fallback
    if (hasSelectAnchor) {
        out_state->caret_x = selX;
        out_state->caret_y = selY - 10;
        out_state->caret_w = 2;
        out_state->caret_h = 20;
        out_state->caret_source = 4;
    } else if (hasIBeam) {
        out_state->caret_x = ibX;
        out_state->caret_y = ibY - 10;
        out_state->caret_w = 2;
        out_state->caret_h = 20;
        out_state->caret_source = 4;
    }
    return 1;
}

GV_API int32_t gv_get_typed_buffer(wchar_t* out_buf, int32_t max_chars) {
    if (!out_buf || max_chars <= 0 || !g_csInitialized) return 0;
    EnterCriticalSection(&g_cs);
    int32_t copyLen = std::min<int32_t>(max_chars - 1, static_cast<int32_t>(g_typedBuffer.size()));
    if (copyLen > 0) {
        memcpy(out_buf, g_typedBuffer.data(), copyLen * sizeof(wchar_t));
    }
    out_buf[copyLen] = L'\0';
    LeaveCriticalSection(&g_cs);
    return copyLen;
}

GV_API void gv_clear_typed_buffer(void) {
    if (!g_csInitialized) return;
    EnterCriticalSection(&g_cs);
    g_typedBuffer.clear();
    LeaveCriticalSection(&g_cs);
}

GV_API void gv_ack_selection_event(void) {
    if (!g_csInitialized) return;
    EnterCriticalSection(&g_cs);
    g_hasSelectionEvent = false;
    LeaveCriticalSection(&g_cs);
}

GV_API int32_t gv_capture_selection(int64_t target_hwnd, wchar_t* out_buf, int32_t max_chars) {
    if (!out_buf || max_chars <= 0) return 0;
    out_buf[0] = L'\0';

    HWND target = reinterpret_cast<HWND>(static_cast<intptr_t>(target_hwnd));
    if (target && IsWindow(target) && GetForegroundWindow() != target) {
        SetForegroundWindow(target);
        Sleep(25);
    }

    std::wstring savedClip;
    bool hadOriginalText = ReadClipboardUnicode(savedClip);
    DWORD seqBefore = GetClipboardSequenceNumber();

    SendCtrlKey('C');

    bool changed = false;
    for (int i = 0; i < 22; ++i) {
        Sleep(10);
        if (GetClipboardSequenceNumber() != seqBefore) {
            changed = true;
            break;
        }
    }

    std::wstring captured;
    if (changed) {
        Sleep(8);
        ReadClipboardUnicode(captured);
    }

    WriteClipboardUnicode(savedClip, hadOriginalText);

    if (captured.empty()) return 0;
    int32_t copyLen = std::min<int32_t>(max_chars - 1, static_cast<int32_t>(captured.size()));
    memcpy(out_buf, captured.data(), copyLen * sizeof(wchar_t));
    out_buf[copyLen] = L'\0';
    return copyLen;
}

GV_API int32_t gv_capture_full_field(int64_t target_hwnd, wchar_t* out_buf, int32_t max_chars) {
    if (!out_buf || max_chars <= 0) return 0;
    out_buf[0] = L'\0';

    HWND target = reinterpret_cast<HWND>(static_cast<intptr_t>(target_hwnd));
    if (target && IsWindow(target) && GetForegroundWindow() != target) {
        SetForegroundWindow(target);
        Sleep(30);
    }

    std::wstring savedClip;
    bool hadOriginalText = ReadClipboardUnicode(savedClip);

    SendCtrlKey('A');
    Sleep(55);

    DWORD seqBefore = GetClipboardSequenceNumber();
    SendCtrlKey('C');

    bool changed = false;
    for (int i = 0; i < 24; ++i) {
        Sleep(10);
        if (GetClipboardSequenceNumber() != seqBefore) {
            changed = true;
            break;
        }
    }

    std::wstring captured;
    if (changed) {
        Sleep(8);
        ReadClipboardUnicode(captured);
    }

    // Collapse selection cleanly to the logical end of the text box (VK_LEFT for RTL Persian/Arabic, VK_RIGHT for LTR)
    bool hasRtlChars = false;
    for (wchar_t ch : captured) {
        if ((ch >= 0x0590 && ch <= 0x08FF) || (ch >= 0xFB50 && ch <= 0xFEFF)) {
            hasRtlChars = true;
            break;
        }
    }
    SendSingleVk(hasRtlChars ? VK_LEFT : VK_RIGHT);
    Sleep(10);

    WriteClipboardUnicode(savedClip, hadOriginalText);

    // Guard: ignore if Ctrl+A grabbed a massive read-only webpage (>3500 chars) rather than an input field
    if (captured.empty() || captured.size() > 3500) return 0;

    if (g_csInitialized) {
        EnterCriticalSection(&g_cs);
        g_typedBuffer = captured;
        LeaveCriticalSection(&g_cs);
    }

    int32_t copyLen = std::min<int32_t>(max_chars - 1, static_cast<int32_t>(captured.size()));
    memcpy(out_buf, captured.data(), copyLen * sizeof(wchar_t));
    out_buf[copyLen] = L'\0';
    return copyLen;
}

GV_API int32_t gv_replace_text(int64_t target_hwnd, const wchar_t* new_text, int32_t replace_mode, int32_t backspace_count) {
    if (!new_text) return 0;
    std::wstring replacement(new_text);

    HWND target = reinterpret_cast<HWND>(static_cast<intptr_t>(target_hwnd));
    if (target && IsWindow(target) && GetForegroundWindow() != target) {
        SetForegroundWindow(target);
        Sleep(35);
    }

    ReleaseModifiersIfHeld();

    if (replace_mode == 0) {
        // Active selection is already highlighted -> clear it cleanly before Unicode stream
        SendSingleVk(VK_BACK);
        Sleep(30);
    } else if (replace_mode == 1 && backspace_count > 0) {
        int count = std::min<int>(backspace_count, 1200);
        int sent = 0;
        while (sent < count) {
            int chunk = std::min<int>(20, count - sent);
            std::vector<INPUT> backs;
            backs.reserve(chunk * 2);
            for (int i = 0; i < chunk; ++i) {
                INPUT down = {};
                down.type = INPUT_KEYBOARD;
                down.ki.wVk = VK_BACK;
                backs.push_back(down);

                INPUT up = {};
                up.type = INPUT_KEYBOARD;
                up.ki.wVk = VK_BACK;
                up.ki.dwFlags = KEYEVENTF_KEYUP;
                backs.push_back(up);
            }
            SendInput(static_cast<UINT>(backs.size()), backs.data(), sizeof(INPUT));
            sent += chunk;
            Sleep(6);
        }
        Sleep(35);
    } else if (replace_mode == 2) {
        SendSingleVk(VK_HOME);
        Sleep(15);
        INPUT selEnd[4] = {};
        selEnd[0].type = INPUT_KEYBOARD; selEnd[0].ki.wVk = VK_SHIFT;
        selEnd[1].type = INPUT_KEYBOARD; selEnd[1].ki.wVk = VK_END;
        selEnd[2].type = INPUT_KEYBOARD; selEnd[2].ki.wVk = VK_END; selEnd[2].ki.dwFlags = KEYEVENTF_KEYUP;
        selEnd[3].type = INPUT_KEYBOARD; selEnd[3].ki.wVk = VK_SHIFT; selEnd[3].ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(4, selEnd, sizeof(INPUT));
        Sleep(35);
        SendSingleVk(VK_BACK);
        Sleep(30);
    } else if (replace_mode == 3) {
        // Select entire input field in Edge / Chrome / Desktop apps and clear cleanly
        SendCtrlKey('A');
        Sleep(60);
        SendSingleVk(VK_BACK);
        Sleep(35);
    }

    // Stream replacement characters directly via KEYEVENTF_UNICODE (zero clipboard races!)
    SendUnicodeTextDirect(replacement);

    if (g_csInitialized) {
        EnterCriticalSection(&g_cs);
        g_typedBuffer = replacement;
        g_hasSelectionEvent = false;
        LeaveCriticalSection(&g_cs);
    }

    return 1;
}

GV_API int32_t gv_insert_text_at_cursor(int64_t target_hwnd, const wchar_t* text) {
    if (!text || !text[0]) return 0;
    std::wstring ins(text);

    HWND target = reinterpret_cast<HWND>(static_cast<intptr_t>(target_hwnd));
    if (target && IsWindow(target) && GetForegroundWindow() != target) {
        SetForegroundWindow(target);
        Sleep(35);
    }

    ReleaseModifiersIfHeld();
    SendUnicodeTextDirect(ins);

    if (g_csInitialized) {
        EnterCriticalSection(&g_cs);
        if (!g_typedBuffer.empty() && g_typedBuffer.back() != L' ') {
            g_typedBuffer.push_back(L' ');
        }
        g_typedBuffer += ins;
        g_lastTypeTick = GetTickCount64();
        g_isTypingActive = true;
        LeaveCriticalSection(&g_cs);
    }
    return 1;
}

namespace {

HWAVEIN g_hWaveIn = NULL;
WAVEHDR g_waveHdr = {};
std::vector<uint8_t> g_waveBuffer;
bool g_isVoiceRecording = false;
const uint32_t kVoiceSampleRate = 16000;
const uint16_t kVoiceChannels = 1;
const uint16_t kVoiceBits = 16;
const uint32_t kMaxRecordSeconds = 60;

} // namespace

GV_API int32_t gv_voice_is_recording(void) {
    return g_isVoiceRecording ? 1 : 0;
}

GV_API int32_t gv_voice_start_recording(void) {
    if (g_isVoiceRecording) return 1;

    WAVEFORMATEX wfx = {};
    wfx.wFormatTag = WAVE_FORMAT_PCM;
    wfx.nChannels = kVoiceChannels;
    wfx.nSamplesPerSec = kVoiceSampleRate;
    wfx.wBitsPerSample = kVoiceBits;
    wfx.nBlockAlign = (kVoiceChannels * kVoiceBits) / 8;
    wfx.nAvgBytesPerSec = kVoiceSampleRate * wfx.nBlockAlign;
    wfx.cbSize = 0;

    MMRESULT mm = waveInOpen(&g_hWaveIn, WAVE_MAPPER, &wfx, 0, 0, CALLBACK_NULL);
    if (mm != MMSYSERR_NOERROR || !g_hWaveIn) {
        g_hWaveIn = NULL;
        return 0;
    }

    uint32_t bufBytes = kVoiceSampleRate * wfx.nBlockAlign * kMaxRecordSeconds;
    g_waveBuffer.assign(bufBytes, 0);

    memset(&g_waveHdr, 0, sizeof(g_waveHdr));
    g_waveHdr.lpData = reinterpret_cast<LPSTR>(g_waveBuffer.data());
    g_waveHdr.dwBufferLength = bufBytes;

    if (waveInPrepareHeader(g_hWaveIn, &g_waveHdr, sizeof(WAVEHDR)) != MMSYSERR_NOERROR) {
        waveInClose(g_hWaveIn);
        g_hWaveIn = NULL;
        return 0;
    }

    if (waveInAddBuffer(g_hWaveIn, &g_waveHdr, sizeof(WAVEHDR)) != MMSYSERR_NOERROR ||
        waveInStart(g_hWaveIn) != MMSYSERR_NOERROR) {
        waveInUnprepareHeader(g_hWaveIn, &g_waveHdr, sizeof(WAVEHDR));
        waveInClose(g_hWaveIn);
        g_hWaveIn = NULL;
        return 0;
    }

    g_isVoiceRecording = true;
    return 1;
}

GV_API int32_t gv_voice_stop_recording(const wchar_t* out_wav_path) {
    if (!g_isVoiceRecording || !g_hWaveIn) {
        g_isVoiceRecording = false;
        return 0;
    }

    waveInStop(g_hWaveIn);
    waveInReset(g_hWaveIn);
    uint32_t recordedBytes = g_waveHdr.dwBytesRecorded;
    waveInUnprepareHeader(g_hWaveIn, &g_waveHdr, sizeof(WAVEHDR));
    waveInClose(g_hWaveIn);
    g_hWaveIn = NULL;
    g_isVoiceRecording = false;

    if (!out_wav_path || !out_wav_path[0] || recordedBytes < 640) {
        return 0;
    }

    HANDLE hFile = CreateFileW(out_wav_path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile == INVALID_HANDLE_VALUE) return 0;

    uint16_t blockAlign = (kVoiceChannels * kVoiceBits) / 8;
    uint32_t byteRate = kVoiceSampleRate * blockAlign;
    uint32_t riffSize = 36 + recordedBytes;
    uint32_t fmtSize = 16;
    uint16_t audioFormat = 1; // PCM

    uint8_t header[44];
    memcpy(header + 0,  "RIFF", 4);
    memcpy(header + 4,  &riffSize, 4);
    memcpy(header + 8,  "WAVE", 4);
    memcpy(header + 12, "fmt ", 4);
    memcpy(header + 16, &fmtSize, 4);
    memcpy(header + 20, &audioFormat, 2);
    memcpy(header + 22, &kVoiceChannels, 2);
    memcpy(header + 24, &kVoiceSampleRate, 4);
    memcpy(header + 28, &byteRate, 4);
    memcpy(header + 32, &blockAlign, 2);
    memcpy(header + 34, &kVoiceBits, 2);
    memcpy(header + 36, "data", 4);
    memcpy(header + 40, &recordedBytes, 4);

    DWORD written1 = 0, written2 = 0;
    WriteFile(hFile, header, 44, &written1, NULL);
    WriteFile(hFile, g_waveBuffer.data(), recordedBytes, &written2, NULL);
    CloseHandle(hFile);

    return (written1 == 44 && written2 == recordedBytes) ? static_cast<int32_t>(recordedBytes) : 0;
}

namespace {

// Windows 10/11 Built-in OS Spell Checker COM ABI (MsSpellCheckingFacility.dll - 100k+ en-US dictionary)
static const GUID kCLSID_SpellCheckerFactory =
    {0x7AB36653, 0x1796, 0x484B, {0xBD, 0xFA, 0xE7, 0x4F, 0x1D, 0xB7, 0xC1, 0xDC}};
static const GUID kIID_ISpellCheckerFactory =
    {0x8E018A9D, 0x2415, 0x4677, {0xBF, 0x08, 0x79, 0x4E, 0xA6, 0x1F, 0x94, 0xBB}};

struct GvISpellingErrorVtbl {
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(void*, REFIID, void**);
    ULONG   (STDMETHODCALLTYPE *AddRef)(void*);
    ULONG   (STDMETHODCALLTYPE *Release)(void*);
    HRESULT (STDMETHODCALLTYPE *get_StartIndex)(void*, ULONG*);
    HRESULT (STDMETHODCALLTYPE *get_Length)(void*, ULONG*);
    HRESULT (STDMETHODCALLTYPE *get_CorrectiveAction)(void*, int*);
    HRESULT (STDMETHODCALLTYPE *get_Replacement)(void*, LPWSTR*);
};

struct GvIEnumSpellingErrorVtbl {
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(void*, REFIID, void**);
    ULONG   (STDMETHODCALLTYPE *AddRef)(void*);
    ULONG   (STDMETHODCALLTYPE *Release)(void*);
    HRESULT (STDMETHODCALLTYPE *Next)(void*, void**);
};

struct GvISpellCheckerVtbl {
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(void*, REFIID, void**);
    ULONG   (STDMETHODCALLTYPE *AddRef)(void*);
    ULONG   (STDMETHODCALLTYPE *Release)(void*);
    HRESULT (STDMETHODCALLTYPE *get_LanguageTag)(void*, LPWSTR*);
    HRESULT (STDMETHODCALLTYPE *Check)(void*, LPCWSTR, void**);
    HRESULT (STDMETHODCALLTYPE *Suggest)(void*, LPCWSTR, IEnumString**);
};

struct GvISpellCheckerFactoryVtbl {
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(void*, REFIID, void**);
    ULONG   (STDMETHODCALLTYPE *AddRef)(void*);
    ULONG   (STDMETHODCALLTYPE *Release)(void*);
    HRESULT (STDMETHODCALLTYPE *get_SupportedLanguages)(void*, IEnumString**);
    HRESULT (STDMETHODCALLTYPE *IsSupported)(void*, LPCWSTR, BOOL*);
    HRESULT (STDMETHODCALLTYPE *CreateSpellChecker)(void*, LPCWSTR, void**);
};

INIT_ONCE g_spellInitOnce = INIT_ONCE_STATIC_INIT;
CRITICAL_SECTION g_spellCs;
void* g_osSpellChecker = NULL;
void* g_osFaSpellChecker = NULL;
bool g_osSpellProbed = false;

bool ContainsPersianOrRtl(const std::wstring& s) {
    for (wchar_t ch : s) {
        if ((ch >= 0x0590 && ch <= 0x08FF) || (ch >= 0xFB50 && ch <= 0xFEFF)) return true;
    }
    return false;
}

BOOL CALLBACK InitSpellCsCallback(PINIT_ONCE, PVOID, PVOID*) {
    InitializeCriticalSection(&g_spellCs);
    return TRUE;
}

void* GetOsSpellCheckerLocked(bool persian = false) {
    if (!g_osSpellProbed) {
        g_osSpellProbed = true;
        CoInitializeEx(NULL, COINIT_MULTITHREADED);
        void* pFactory = NULL;
        HRESULT hr = CoCreateInstance(kCLSID_SpellCheckerFactory, NULL, CLSCTX_INPROC_SERVER,
                                      kIID_ISpellCheckerFactory, &pFactory);
        if (SUCCEEDED(hr) && pFactory) {
            auto* facVtbl = *reinterpret_cast<GvISpellCheckerFactoryVtbl**>(pFactory);
            void* pChecker = NULL;
            hr = facVtbl->CreateSpellChecker(pFactory, L"en-US", &pChecker);
            if (FAILED(hr) || !pChecker) {
                hr = facVtbl->CreateSpellChecker(pFactory, L"en-GB", &pChecker);
            }
            if (SUCCEEDED(hr) && pChecker) {
                g_osSpellChecker = pChecker;
            }

            void* pFaChecker = NULL;
            hr = facVtbl->CreateSpellChecker(pFactory, L"fa-IR", &pFaChecker);
            if (FAILED(hr) || !pFaChecker) {
                hr = facVtbl->CreateSpellChecker(pFactory, L"fa", &pFaChecker);
            }
            if (SUCCEEDED(hr) && pFaChecker) {
                g_osFaSpellChecker = pFaChecker;
            }
            facVtbl->Release(pFactory);
        }
    }
    return persian ? g_osFaSpellChecker : g_osSpellChecker;
}

bool OsCheckIsWordValidLocked(void* pChecker, const std::wstring& probe) {
    if (!pChecker || probe.empty()) return false;
    auto* chkVtbl = *reinterpret_cast<GvISpellCheckerVtbl**>(pChecker);
    void* pEnum = NULL;
    if (FAILED(chkVtbl->Check(pChecker, probe.c_str(), &pEnum)) || !pEnum) return true;

    auto* enumVtbl = *reinterpret_cast<GvIEnumSpellingErrorVtbl**>(pEnum);
    void* pErr = NULL;
    HRESULT hr = enumVtbl->Next(pEnum, &pErr);
    bool valid = true;
    if (hr == S_OK && pErr) {
        valid = false;
        auto* errVtbl = *reinterpret_cast<GvISpellingErrorVtbl**>(pErr);
        errVtbl->Release(pErr);
    }
    enumVtbl->Release(pEnum);
    return valid;
}

std::wstring CollapseRuns(const std::wstring& s, size_t maxRun) {
    std::wstring out;
    out.reserve(s.size());
    size_t run = 0;
    wchar_t prev = 0;
    for (wchar_t c : s) {
        if (c == prev) {
            run++;
            if (run <= maxRun) out.push_back(c);
        } else {
            prev = c;
            run = 1;
            out.push_back(c);
        }
    }
    return out;
}

} // namespace

GV_API int32_t gv_damerau_distance(const wchar_t* a, const wchar_t* b) {
    if (!a || !b) return -1;
    std::wstring sa(a), sb(b);
    for (auto& c : sa) c = static_cast<wchar_t>(std::towlower(c));
    for (auto& c : sb) c = static_cast<wchar_t>(std::towlower(c));
    return ComputeDamerau(sa, sb);
}

GV_API int32_t gv_is_valid_word(const wchar_t* word) {
    if (!word || !word[0]) return 0;
    std::wstring orig(word);
    if (orig.size() > 40) return 0;
    bool isRtl = ContainsPersianOrRtl(orig);
    for (wchar_t c : orig) {
        if (!std::iswalpha(c) && c != L'\'' && c != 0x200C && !((c >= 0x0600 && c <= 0x06FF))) return 0;
    }

    InitOnceExecuteOnce(&g_spellInitOnce, InitSpellCsCallback, NULL, NULL);
    EnterCriticalSection(&g_spellCs);
    CoInitializeEx(NULL, COINIT_MULTITHREADED);
    void* pChecker = GetOsSpellCheckerLocked(isRtl);
    if (pChecker) {
        bool ok = OsCheckIsWordValidLocked(pChecker, orig);
        if (!ok && !isRtl) {
            std::wstring low = orig;
            for (auto& ch : low) ch = static_cast<wchar_t>(std::towlower(ch));
            if (low != orig) ok = OsCheckIsWordValidLocked(pChecker, low);
        }
        LeaveCriticalSection(&g_spellCs);
        return ok ? 1 : 0;
    }
    LeaveCriticalSection(&g_spellCs);
    if (isRtl) return 1;

    // Fallback lexicon check if OS spellchecker unavailable
    std::wstring low = orig;
    for (auto& ch : low) ch = static_cast<wchar_t>(std::towlower(ch));
    const size_t lexCount = sizeof(kCoreLexicon) / sizeof(kCoreLexicon[0]);
    for (size_t i = 0; i < lexCount; ++i) {
        std::wstring c(kCoreLexicon[i]);
        for (auto& ch : c) ch = static_cast<wchar_t>(std::towlower(ch));
        if (c == low) return 1;
    }
    return 0;
}

GV_API int32_t gv_spell_check_word(const wchar_t* word, wchar_t* out_suggestion, int32_t max_chars) {
    if (!word || !out_suggestion || max_chars <= 1) return 0;
    out_suggestion[0] = L'\0';

    std::wstring orig(word);
    if (orig.size() < 2 || orig.size() > 32) return 0;
    bool isRtl = ContainsPersianOrRtl(orig);
    // Windows fa-IR ISpellChecker does not understand ZWNJ (0x200C) compounds and strips them
    if (isRtl && orig.find(static_cast<wchar_t>(0x200C)) != std::wstring::npos) return 0;
    for (wchar_t c : orig) {
        if (!std::iswalpha(c) && c != 0x200C && !(c >= 0x0600 && c <= 0x06FF)) return 0;
    }

    std::wstring lower = orig;
    if (!isRtl) {
        for (auto& c : lower) c = static_cast<wchar_t>(std::towlower(c));
    }

    std::wstring bestCand;

    // 1. Exact curated high-precision typo lookup (covers 3..15 char common English typos immediately)
    if (!isRtl) {
        const size_t typoCount = sizeof(kDirectTypos) / sizeof(kDirectTypos[0]);
        for (size_t i = 0; i < typoCount; ++i) {
            if (lower == kDirectTypos[i].bad) {
                if (lower == kDirectTypos[i].good) return 0;
                bestCand = kDirectTypos[i].good;
                break;
            }
        }
    }

    // 2. Windows 10/11 Built-in OS ISpellChecker (en-US or fa-IR if language pack installed)
    bool handledByOsChecker = false;
    if (bestCand.empty()) {
        InitOnceExecuteOnce(&g_spellInitOnce, InitSpellCsCallback, NULL, NULL);
        EnterCriticalSection(&g_spellCs);
        CoInitializeEx(NULL, COINIT_MULTITHREADED);
        void* pChecker = GetOsSpellCheckerLocked(isRtl);
        if (pChecker) {
            handledByOsChecker = true;
            if (OsCheckIsWordValidLocked(pChecker, orig) || OsCheckIsWordValidLocked(pChecker, lower)) {
                LeaveCriticalSection(&g_spellCs);
                return 0; // Valid English word in OS dictionary!
            }

            // 2a. Repeated-character keystroke slip normalization (e.g. "dddo" -> "do", "helllo" -> "hello", "meaan" -> "mean")
            std::wstring col1 = CollapseRuns(lower, 1);
            std::wstring col2 = CollapseRuns(lower, 2);
            if (col1 != lower && col1.size() >= 2 && OsCheckIsWordValidLocked(pChecker, col1)) {
                bestCand = col1;
            } else if (col2 != lower && col2.size() >= 3 && OsCheckIsWordValidLocked(pChecker, col2)) {
                bestCand = col2;
            } else {
                // Single accidental doubled letter (e.g. "meaan" -> "mean", "whaat" -> "what", "thiis" -> "this")
                for (size_t i = 0; i + 1 < lower.size(); ++i) {
                    if (lower[i] == lower[i + 1]) {
                        std::wstring cand = lower.substr(0, i) + lower.substr(i + 1);
                        if (cand.size() >= 2 && OsCheckIsWordValidLocked(pChecker, cand)) {
                            bestCand = cand;
                            break;
                        }
                    }
                }
            }

            // 2b. Query OS ISpellChecker::Check auto-replace or ISpellChecker::Suggest top dictionary match
            if (bestCand.empty()) {
                auto* chkVtbl = *reinterpret_cast<GvISpellCheckerVtbl**>(pChecker);
                void* pEnumErr = NULL;
                if (SUCCEEDED(chkVtbl->Check(pChecker, lower.c_str(), &pEnumErr)) && pEnumErr) {
                    auto* enumErrVtbl = *reinterpret_cast<GvIEnumSpellingErrorVtbl**>(pEnumErr);
                    void* pErr = NULL;
                    if (enumErrVtbl->Next(pEnumErr, &pErr) == S_OK && pErr) {
                        auto* errVtbl = *reinterpret_cast<GvISpellingErrorVtbl**>(pErr);
                        int action = 0;
                        if (SUCCEEDED(errVtbl->get_CorrectiveAction(pErr, &action)) && action == 2) {
                            LPWSTR rep = NULL;
                            if (SUCCEEDED(errVtbl->get_Replacement(pErr, &rep)) && rep) {
                                if (rep[0]) bestCand = rep;
                                CoTaskMemFree(rep);
                            }
                        }
                        errVtbl->Release(pErr);
                    }
                    enumErrVtbl->Release(pEnumErr);
                }

                // Only use blind top-1 Suggest() for English (fa-IR Hunspell lacks colloquial & clitic forms)
                if (bestCand.empty() && !isRtl) {
                    IEnumString* pEnumStr = NULL;
                    if (SUCCEEDED(chkVtbl->Suggest(pChecker, lower.c_str(), &pEnumStr)) && pEnumStr) {
                        LPOLESTR sugStr = NULL;
                        ULONG fetched = 0;
                        if (pEnumStr->Next(1, &sugStr, &fetched) == S_OK && sugStr) {
                            if (sugStr[0]) bestCand = sugStr;
                            CoTaskMemFree(sugStr);
                        }
                        pEnumStr->Release();
                    }
                }
            }
        }
        LeaveCriticalSection(&g_spellCs);
    }

    // 3. Fallback if OS ISpellChecker was unavailable: Damerau-Levenshtein distance=1 over built-in lexicon
    if (!isRtl && bestCand.empty() && !handledByOsChecker && lower.size() >= 6) {
        const size_t lexCount = sizeof(kCoreLexicon) / sizeof(kCoreLexicon[0]);

        auto isInLexicon = [&](const std::wstring& probe) -> bool {
            for (size_t i = 0; i < lexCount; ++i) {
                std::wstring c(kCoreLexicon[i]);
                for (auto& ch : c) ch = static_cast<wchar_t>(std::towlower(ch));
                if (c == probe) return true;
            }
            return false;
        };

        if (isInLexicon(lower)) return 0;
        if (lower.size() > 4 && lower.back() == L's' && isInLexicon(lower.substr(0, lower.size() - 1))) return 0;
        if (lower.size() > 5 && lower.substr(lower.size() - 2) == L"es" && isInLexicon(lower.substr(0, lower.size() - 2))) return 0;
        if (lower.size() > 5 && lower.substr(lower.size() - 2) == L"ed" &&
            (isInLexicon(lower.substr(0, lower.size() - 2)) || isInLexicon(lower.substr(0, lower.size() - 1)))) return 0;
        if (lower.size() > 5 && lower.substr(lower.size() - 2) == L"ly" && isInLexicon(lower.substr(0, lower.size() - 2))) return 0;
        if (lower.size() > 6 && lower.substr(lower.size() - 3) == L"ing" &&
            (isInLexicon(lower.substr(0, lower.size() - 3)) || isInLexicon(lower.substr(0, lower.size() - 3) + L"e"))) return 0;

        int bestScore = 9999;
        for (size_t i = 0; i < lexCount; ++i) {
            std::wstring cand(kCoreLexicon[i]);
            std::wstring candLower = cand;
            for (auto& c : candLower) c = static_cast<wchar_t>(std::towlower(c));

            int lenDiff = std::abs(static_cast<int>(candLower.size()) - static_cast<int>(lower.size()));
            if (lenDiff > 1) continue;
            if (candLower[0] != lower[0]) continue;

            int dist = ComputeDamerau(lower, candLower);
            if (dist == 1) {
                int score = dist * 100 + lenDiff * 15;
                if (candLower.back() != lower.back()) score += 10;
                if (score < bestScore) {
                    bestScore = score;
                    bestCand = cand;
                }
            }
        }
    }

    if (bestCand.empty()) return 0;

    // Match original capitalization pattern
    bool allUpper = true;
    for (wchar_t c : orig) {
        if (!std::iswupper(c)) { allUpper = false; break; }
    }
    if (allUpper && orig.size() > 1) {
        for (auto& c : bestCand) c = static_cast<wchar_t>(std::towupper(c));
    } else if (std::iswupper(orig[0])) {
        bestCand[0] = static_cast<wchar_t>(std::towupper(bestCand[0]));
    }

    int32_t copyLen = std::min<int32_t>(max_chars - 1, static_cast<int32_t>(bestCand.size()));
    memcpy(out_suggestion, bestCand.data(), copyLen * sizeof(wchar_t));
    out_suggestion[copyLen] = L'\0';
    return 1;
}

// ---------------------------------------------------------------------------
// Windows Copilot+ PC Phi Silica NPU Bridge (WinRT COM ABI)
// ---------------------------------------------------------------------------
namespace {

typedef void* GV_HSTRING;
typedef HRESULT (WINAPI *PFN_DllGetActivationFactory)(GV_HSTRING, IUnknown**);
typedef HRESULT (WINAPI *PFN_WindowsCreateString)(PCWSTR, UINT32, GV_HSTRING*);
typedef HRESULT (WINAPI *PFN_WindowsDeleteString)(GV_HSTRING);
typedef PCWSTR  (WINAPI *PFN_WindowsGetStringRawBuffer)(GV_HSTRING, UINT32*);

static const GUID kIID_ILanguageModelStatics =
    {0x8f18f9af, 0x6095, 0x553b, {0x8d, 0x9d, 0x6b, 0xcc, 0x98, 0x02, 0x65, 0x46}};
static const GUID kIID_ILanguageModel2 =
    {0x653b714e, 0xf9b3, 0x51cb, {0x95, 0x4f, 0x5e, 0xa5, 0x8f, 0x63, 0xab, 0x89}};
static const GUID kIID_ILanguageModelResponseResult =
    {0x3a256fff, 0xa426, 0x5d3b, {0x8e, 0x4b, 0x3a, 0xc8, 0x41, 0x62, 0x47, 0x1e}};
static const GUID kIID_ITextRewriterFactory =
    {0xf452e60d, 0xef50, 0x5bc9, {0xb4, 0x83, 0x21, 0x7d, 0x5b, 0x4e, 0x71, 0x51}};
static const GUID kIID_ITextRewriter2 =
    {0x7937d261, 0x13ce, 0x5b24, {0x95 - 0x1e + 0x1e, 0x7c, 0xfe, 0x5c, 0xd0, 0xbe, 0x23, 0xb6}};
static const GUID kIID_ITextRewriter2_Exact =
    {0x7937d261, 0x13ce, 0x5b24, {0x91 + 0x00, 0x7c, 0xfe, 0x5c, 0xd0, 0xbe, 0x23, 0xb6}};
static const GUID kIID_ITextRewriter3 =
    {0x622eb80a, 0xed1a, 0x5525, {0x80, 0xd8, 0xc6, 0xe8, 0x02, 0x36, 0xa1, 0x95}};
static const GUID kIID_IAsyncInfo =
    {0x00000036, 0x0000, 0x0000, {0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46}};

struct IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *QueryInterface)(void*, REFIID, void**);
    ULONG   (STDMETHODCALLTYPE *AddRef)(void*);
    ULONG   (STDMETHODCALLTYPE *Release)(void*);
    HRESULT (STDMETHODCALLTYPE *GetIids)(void*, ULONG*, IID**);
    HRESULT (STDMETHODCALLTYPE *GetRuntimeClassName)(void*, GV_HSTRING*);
    HRESULT (STDMETHODCALLTYPE *GetTrustLevel)(void*, int*);
};

struct ILanguageModelStaticsVtbl : IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *GetReadyState)(void*, int*);
    HRESULT (STDMETHODCALLTYPE *EnsureReadyAsync)(void*, IUnknown**);
    HRESULT (STDMETHODCALLTYPE *CreateAsync)(void*, IUnknown**);
};

struct ILanguageModel2Vtbl : IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *GenerateResponseAsync)(void*, GV_HSTRING, IUnknown**);
};

struct ITextRewriterFactoryVtbl : IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *CreateInstance)(void*, IUnknown*, IUnknown**);
};

struct ITextRewriter3Vtbl : IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *RewriteCustomAsync)(void*, GV_HSTRING, GV_HSTRING, IUnknown**);
};

struct ILanguageModelResponseResultVtbl : IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *get_Text)(void*, GV_HSTRING*);
    HRESULT (STDMETHODCALLTYPE *get_Status)(void*, int*);
};

struct IAsyncInfoVtbl : IInspectableVtbl {
    HRESULT (STDMETHODCALLTYPE *get_Id)(void*, UINT32*);
    HRESULT (STDMETHODCALLTYPE *get_Status)(void*, int*);
    HRESULT (STDMETHODCALLTYPE *get_ErrorCode)(void*, HRESULT*);
};

// Generic vtable helper for IAsyncOperation<T> (slot 8) & IAsyncOperationWithProgress<T,P> (slot 10)
struct IAsyncOpVtbl : IInspectableVtbl {
    void* slot6;
    void* slot7;
    HRESULT (STDMETHODCALLTYPE *GetResults_Op)(void*, IUnknown**);
    void* slot9;
    HRESULT (STDMETHODCALLTYPE *GetResults_OpProgress)(void*, IUnknown**);
};

HMODULE g_phiTextDll = NULL;
PFN_DllGetActivationFactory g_pfnPhiFactory = NULL;
PFN_WindowsCreateString g_pfnCreateStr = NULL;
PFN_WindowsDeleteString g_pfnDeleteStr = NULL;
PFN_WindowsGetStringRawBuffer g_pfnGetStrBuf = NULL;
IUnknown* g_cachedLanguageModel = NULL;

bool EnsurePhiDllLoaded() {
    if (g_pfnPhiFactory && g_pfnCreateStr) return true;

    HMODULE hCombase = LoadLibraryW(L"combase.dll");
    if (!hCombase) return false;
    g_pfnCreateStr = reinterpret_cast<PFN_WindowsCreateString>(GetProcAddress(hCombase, "WindowsCreateString"));
    g_pfnDeleteStr = reinterpret_cast<PFN_WindowsDeleteString>(GetProcAddress(hCombase, "WindowsDeleteString"));
    g_pfnGetStrBuf = reinterpret_cast<PFN_WindowsGetStringRawBuffer>(GetProcAddress(hCombase, "WindowsGetStringRawBuffer"));
    if (!g_pfnCreateStr || !g_pfnDeleteStr || !g_pfnGetStrBuf) return false;

    const wchar_t* dirs[] = {
        L"C:\\Windows\\SystemApps\\Microsoft.WindowsAppRuntime.CBS.2_8wekyb3d8bbwe",
        L"C:\\Windows\\SystemApps\\Microsoft.WindowsAppRuntime.CBS_8wekyb3d8bbwe"
    };
    for (const wchar_t* dir : dirs) {
        std::wstring full = std::wstring(dir) + L"\\Microsoft.Windows.AI.Text.dll";
        if (GetFileAttributesW(full.c_str()) != INVALID_FILE_ATTRIBUTES) {
            SetDllDirectoryW(dir);
            g_phiTextDll = LoadLibraryW(full.c_str());
            if (g_phiTextDll) {
                g_pfnPhiFactory = reinterpret_cast<PFN_DllGetActivationFactory>(
                    GetProcAddress(g_phiTextDll, "DllGetActivationFactory"));
                if (g_pfnPhiFactory) return true;
            }
        }
    }
    return false;
}

bool AwaitWinRtOperation(IUnknown* pOp, bool hasProgress, IUnknown** outResult, DWORD timeoutMs) {
    if (!pOp || !outResult) return false;
    *outResult = NULL;

    void* pInfoRaw = NULL;
    if (FAILED(pOp->QueryInterface(kIID_IAsyncInfo, &pInfoRaw)) || !pInfoRaw) return false;
    auto* infoVtbl = *reinterpret_cast<IAsyncInfoVtbl**>(pInfoRaw);

    DWORD waited = 0;
    int status = 0;
    while (waited < timeoutMs) {
        if (FAILED(infoVtbl->get_Status(pInfoRaw, &status)) || status != 0) break;
        Sleep(15);
        waited += 15;
    }
    infoVtbl->Release(pInfoRaw);
    if (status != 1) return false; // 1 == AsyncStatus::Completed

    auto* inspVtbl = *reinterpret_cast<IInspectableVtbl**>(pOp);
    ULONG iidCount = 0;
    IID* iids = NULL;
    if (FAILED(inspVtbl->GetIids(pOp, &iidCount, &iids)) || !iids) return false;

    bool ok = false;
    for (ULONG i = 0; i < iidCount; ++i) {
        if (memcmp(&iids[i], &kIID_IAsyncInfo, sizeof(GUID)) == 0) continue;
        void* pTyped = NULL;
        if (SUCCEEDED(pOp->QueryInterface(iids[i], &pTyped)) && pTyped) {
            auto* opVtbl = *reinterpret_cast<IAsyncOpVtbl**>(pTyped);
            HRESULT hr = hasProgress
                ? opVtbl->GetResults_OpProgress(pTyped, outResult)
                : opVtbl->GetResults_Op(pTyped, outResult);
            opVtbl->Release(pTyped);
            if (SUCCEEDED(hr) && *outResult) {
                ok = true;
                break;
            }
        }
    }
    CoTaskMemFree(iids);
    return ok;
}

} // namespace

GV_API int32_t gv_phi_silica_status(void) {
    CoInitializeEx(NULL, COINIT_MULTITHREADED);
    if (!EnsurePhiDllLoaded()) return -1;

    GV_HSTRING hs = NULL;
    const wchar_t* cls = L"Microsoft.Windows.AI.Text.LanguageModel";
    if (FAILED(g_pfnCreateStr(cls, static_cast<UINT32>(wcslen(cls)), &hs))) return -1;

    IUnknown* pFac = NULL;
    HRESULT hr = g_pfnPhiFactory(hs, &pFac);
    g_pfnDeleteStr(hs);
    if (FAILED(hr) || !pFac) return -1;

    void* pStatics = NULL;
    hr = pFac->QueryInterface(kIID_ILanguageModelStatics, &pStatics);
    pFac->Release();
    if (FAILED(hr) || !pStatics) return -1;

    auto* vtbl = *reinterpret_cast<ILanguageModelStaticsVtbl**>(pStatics);
    int state = -1;
    hr = vtbl->GetReadyState(pStatics, &state);
    vtbl->Release(pStatics);
    return SUCCEEDED(hr) ? state : -1;
}

GV_API int32_t gv_phi_silica_rewrite(const wchar_t* text, int32_t tone_mode, const wchar_t* custom_instruction,
                                     wchar_t* out_buf, int32_t max_chars, int32_t* out_elapsed_ms) {
    if (!text || !out_buf || max_chars <= 1) return 0;
    out_buf[0] = L'\0';
    if (out_elapsed_ms) *out_elapsed_ms = 0;

    LARGE_INTEGER freq, t0, t1;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&t0);

    CoInitializeEx(NULL, COINIT_MULTITHREADED);
    if (!EnsurePhiDllLoaded()) return 0;

    // Lazily create and cache Phi Silica LanguageModel NPU session
    if (!g_cachedLanguageModel) {
        GV_HSTRING hs = NULL;
        const wchar_t* cls = L"Microsoft.Windows.AI.Text.LanguageModel";
        g_pfnCreateStr(cls, static_cast<UINT32>(wcslen(cls)), &hs);
        IUnknown* pFac = NULL;
        HRESULT hr = g_pfnPhiFactory(hs, &pFac);
        g_pfnDeleteStr(hs);
        if (FAILED(hr) || !pFac) return 0;

        void* pStatics = NULL;
        hr = pFac->QueryInterface(kIID_ILanguageModelStatics, &pStatics);
        pFac->Release();
        if (FAILED(hr) || !pStatics) return 0;

        auto* statVtbl = *reinterpret_cast<ILanguageModelStaticsVtbl**>(pStatics);
        int ready = -1;
        statVtbl->GetReadyState(pStatics, &ready);
        if (ready != 0) {
            statVtbl->Release(pStatics);
            return 0;
        }

        IUnknown* pCreateOp = NULL;
        hr = statVtbl->CreateAsync(pStatics, &pCreateOp);
        statVtbl->Release(pStatics);
        if (FAILED(hr) || !pCreateOp) return 0;

        IUnknown* pModel = NULL;
        bool created = AwaitWinRtOperation(pCreateOp, false, &pModel, 8000);
        pCreateOp->Release();
        if (!created || !pModel) return 0;
        g_cachedLanguageModel = pModel;
    }

    // Build Phi Silica prompt or TextRewriter call
    std::wstring promptStr;
    if (custom_instruction && custom_instruction[0] != L'\0') {
        promptStr = std::wstring(custom_instruction) +
                    L"\nOutput ONLY the final rewritten text with no quotes or explanation.\n\nText: " +
                    std::wstring(text);
    } else {
        const wchar_t* toneDesc = L"smooth, natural, and grammatically correct";
        if (tone_mode == 2) toneDesc = L"warm, casual, and conversational";
        else if (tone_mode == 3) toneDesc = L"concise, direct, and brief";
        else if (tone_mode == 4) toneDesc = L"formal, polished, and professional";
        promptStr = std::wstring(L"Rewrite the following text to be ") + toneDesc +
                    L". Output ONLY the rewritten text with no quotes or explanation.\n\nText: " +
                    std::wstring(text);
    }

    void* pLm2 = NULL;
    if (FAILED(g_cachedLanguageModel->QueryInterface(kIID_ILanguageModel2, &pLm2)) || !pLm2) {
        return 0;
    }
    auto* lm2Vtbl = *reinterpret_cast<ILanguageModel2Vtbl**>(pLm2);
    GV_HSTRING hPrompt = NULL;
    g_pfnCreateStr(promptStr.c_str(), static_cast<UINT32>(promptStr.size()), &hPrompt);

    IUnknown* pGenOp = NULL;
    HRESULT hr = lm2Vtbl->GenerateResponseAsync(pLm2, hPrompt, &pGenOp);
    g_pfnDeleteStr(hPrompt);
    lm2Vtbl->Release(pLm2);
    if (FAILED(hr) || !pGenOp) return 0;

    IUnknown* pRespUnk = NULL;
    bool genOk = AwaitWinRtOperation(pGenOp, true, &pRespUnk, 12000);
    pGenOp->Release();
    if (!genOk || !pRespUnk) return 0;

    void* pResp = NULL;
    std::wstring finalOut;
    if (SUCCEEDED(pRespUnk->QueryInterface(kIID_ILanguageModelResponseResult, &pResp)) && pResp) {
        auto* respVtbl = *reinterpret_cast<ILanguageModelResponseResultVtbl**>(pResp);
        GV_HSTRING hOut = NULL;
        if (SUCCEEDED(respVtbl->get_Text(pResp, &hOut)) && hOut) {
            UINT32 outLen = 0;
            PCWSTR raw = g_pfnGetStrBuf(hOut, &outLen);
            if (raw && outLen > 0) {
                finalOut.assign(raw, outLen);
            }
            g_pfnDeleteStr(hOut);
        }
        respVtbl->Release(pResp);
    }
    pRespUnk->Release();

    // Trim surrounding quotes/whitespace
    while (!finalOut.empty() && (finalOut.front() == L' ' || finalOut.front() == L'\n' || finalOut.front() == L'\r' || finalOut.front() == L'"'))
        finalOut.erase(finalOut.begin());
    while (!finalOut.empty() && (finalOut.back() == L' ' || finalOut.back() == L'\n' || finalOut.back() == L'\r' || finalOut.back() == L'"'))
        finalOut.pop_back();

    QueryPerformanceCounter(&t1);
    if (out_elapsed_ms && freq.QuadPart > 0) {
        *out_elapsed_ms = static_cast<int32_t>(((t1.QuadPart - t0.QuadPart) * 1000) / freq.QuadPart);
    }

    if (finalOut.empty()) return 0;
    int32_t copyLen = std::min<int32_t>(max_chars - 1, static_cast<int32_t>(finalOut.size()));
    memcpy(out_buf, finalOut.data(), copyLen * sizeof(wchar_t));
    out_buf[copyLen] = L'\0';
    return 1;
}

} // extern "C"
