#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
FERDOWSI Python NLP, Rewording, Formatting, AI Dictionary & Optional Local-LLM Engine.
Communicates with FERDOWSI.exe (C#) via UTF-8 JSON-RPC over stdin/stdout,
and accelerates Damerau-Levenshtein spell checking via grammerv_native.dll (C++).
"""

import ctypes
import difflib
import http.client
import json
import os
import re
import socket
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Dict, List, Optional, Tuple


# ---------------------------------------------------------------------------
# Native C++ DLL Bridge (ctypes -> grammerv_native.dll + Phi Silica NPU)
# ---------------------------------------------------------------------------
PHI_STATE_LABELS = {
    -1: "Unavailable",
    0: "Ready (Copilot+ NPU)",
    1: "NotReady (Downloading)",
    2: "NotSupported",
    3: "DisabledByUser",
    4: "Installed (Needs MSIX Identity)",
    5: "HardwareIncompatible",
    6: "OSUpdateNeeded",
}


class NativeCppSpellBridge:
    def __init__(self) -> None:
        self.dll = None
        self.available = False
        self.has_phi_exports = False
        self._load()

    def _load(self) -> None:
        here = os.path.dirname(os.path.abspath(__file__))
        candidates = [
            os.environ.get("GRAMMERV_DLL_PATH", ""),
            os.path.join(here, "grammerv_native.dll"),
            os.path.join(os.path.dirname(here), "grammerv_native.dll"),
            os.path.join(os.path.dirname(os.path.dirname(here)), "grammerv_native.dll"),
        ]
        for path in candidates:
            if path and os.path.isfile(path):
                try:
                    dll = ctypes.WinDLL(path)
                    dll.gv_spell_check_word.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_int32]
                    dll.gv_spell_check_word.restype = ctypes.c_int32
                    dll.gv_damerau_distance.argtypes = [ctypes.c_wchar_p, ctypes.c_wchar_p]
                    dll.gv_damerau_distance.restype = ctypes.c_int32
                    try:
                        dll.gv_is_valid_word.argtypes = [ctypes.c_wchar_p]
                        dll.gv_is_valid_word.restype = ctypes.c_int32
                        self.has_valid_word_export = True
                    except Exception:
                        self.has_valid_word_export = False
                    try:
                        dll.gv_phi_silica_status.argtypes = []
                        dll.gv_phi_silica_status.restype = ctypes.c_int32
                        dll.gv_phi_silica_rewrite.argtypes = [
                            ctypes.c_wchar_p, ctypes.c_int32, ctypes.c_wchar_p,
                            ctypes.c_wchar_p, ctypes.c_int32, ctypes.POINTER(ctypes.c_int32)
                        ]
                        dll.gv_phi_silica_rewrite.restype = ctypes.c_int32
                        self.has_phi_exports = True
                    except Exception:
                        self.has_phi_exports = False
                    self.dll = dll
                    self.available = True
                    return
                except Exception:
                    continue

    def is_valid_word(self, word: str) -> bool:
        if not word:
            return False
        low = word.lower()
        if low in SAFE_VALID_WORDS or low in PROPER_CAPITALIZATION:
            return True
        if self.available and self.dll and getattr(self, "has_valid_word_export", False):
            return int(self.dll.gv_is_valid_word(word)) == 1
        return True

    def suggest(self, word: str) -> Optional[str]:
        if not self.available or not self.dll or len(word) < 2:
            return None
        buf = ctypes.create_unicode_buffer(64)
        res = self.dll.gv_spell_check_word(word, buf, 64)
        if res == 1 and buf.value:
            return buf.value
        return None

    def phi_status(self) -> Tuple[int, str]:
        if not self.available or not self.dll or not self.has_phi_exports:
            return -1, "Unavailable"
        try:
            code = int(self.dll.gv_phi_silica_status())
            return code, PHI_STATE_LABELS.get(code, f"State({code})")
        except Exception:
            return -1, "Unavailable"

    def phi_rewrite(self, text: str, tone_mode: int, instruction: str = "") -> Optional[Tuple[str, int]]:
        if not self.available or not self.dll or not self.has_phi_exports:
            return None
        code, _ = self.phi_status()
        if code != 0:
            return None
        buf = ctypes.create_unicode_buffer(4096)
        elapsed = ctypes.c_int32(0)
        ok = self.dll.gv_phi_silica_rewrite(text, tone_mode, instruction, buf, 4096, ctypes.byref(elapsed))
        if ok == 1 and buf.value:
            return buf.value, int(elapsed.value)
        return None


CPP_BRIDGE = NativeCppSpellBridge()


# ---------------------------------------------------------------------------
# Curated Linguistic Knowledge Base
# ---------------------------------------------------------------------------
COMMON_MISSPELLINGS: Dict[str, str] = {
    "teh": "the", "hte": "the", "taht": "that", "thta": "that",
    "wiht": "with", "iwth": "with", "waht": "what", "hwat": "what",
    "wich": "which", "wihch": "which", "whcih": "which", "whn": "when",
    "wehr": "where", "thier": "their", "theri": "their", "tihs": "this",
    "htis": "this", "thsi": "this", "yuor": "your", "yoru": "your",
    "yuo": "you", "u": "you", "ur": "your", "pls": "please", "plz": "please",
    "thx": "thanks", "cuz": "because", "coz": "because", "bcuz": "because",
    "becuase": "because", "beacuse": "because", "becasue": "because", "ebcause": "because",
    "abuot": "about", "abotu": "about", "fomr": "from", "frmo": "from",
    "ahve": "have", "hvae": "have", "haev": "have", "jsut": "just", "jstu": "just",
    "konw": "know", "knwo": "know", "nkow": "know", "liek": "like", "lkie": "like",
    "amke": "make", "mkae": "make", "moer": "more", "mroe": "more",
    "olny": "only", "onlu": "only", "otehr": "other", "ohter": "other",
    "peopel": "people", "poeple": "people", "peolpe": "people",
    "shoudl": "should", "shuold": "should", "woudl": "would", "wuold": "would",
    "coudl": "could", "cuold": "could", "thign": "thing", "hting": "thing",
    "tihnk": "think", "thnik": "think", "tiem": "time", "tmie": "time",
    "vrey": "very", "veyr": "very", "wnat": "want", "watn": "want",
    "wrok": "work", "owrk": "work", "wrold": "world", "wolrd": "world",
    "alos": "also", "aslo": "also", "abck": "back", "bcak": "back",
    "grammer": "grammar", "gramar": "grammar", "grammerly": "Grammarly",
    "cursur": "cursor", "curser": "cursor",
    "requiered": "required", "requried": "required", "requird": "required",
    "recieve": "receive", "recieved": "received", "recieving": "receiving",
    "beleive": "believe", "beleived": "believed", "acheive": "achieve", "acheived": "achieved",
    "wierd": "weird", "freind": "friend", "freinds": "friends",
    "definately": "definitely", "definitly": "definitely", "definetly": "definitely",
    "seperate": "separate", "seperated": "separated", "seperately": "separately",
    "occured": "occurred", "occuring": "occurring", "occassion": "occasion",
    "untill": "until", "adress": "address", "accomodate": "accommodate",
    "arguement": "argument", "begining": "beginning", "buisness": "business",
    "calender": "calendar", "collegue": "colleague", "comming": "coming",
    "completly": "completely", "concious": "conscious", "curiousity": "curiosity",
    "dissapear": "disappear", "embarass": "embarrass", "embarassing": "embarrassing",
    "enviroment": "environment", "existance": "existence", "familar": "familiar",
    "finaly": "finally", "foriegn": "foreign", "fourty": "forty", "foward": "forward",
    "goverment": "government", "garantee": "guarantee", "gaurantee": "guarantee",
    "happend": "happened", "harrass": "harass", "humourous": "humorous",
    "immediatly": "immediately", "independant": "independent", "interupt": "interrupt",
    "knowlege": "knowledge", "liason": "liaison", "maintenence": "maintenance",
    "neccessary": "necessary", "necesary": "necessary", "noticable": "noticeable",
    "parliment": "parliament", "persistant": "persistent", "posession": "possession",
    "prefered": "preferred", "priviledge": "privilege",
    "proffesional": "professional", "profesional": "professional",
    "publically": "publicly", "questionaire": "questionnaire",
    "realyl": "really", "realy": "really", "recomend": "recommend", "reccomend": "recommend",
    "refered": "referred", "religous": "religious", "remeber": "remember", "rember": "remember",
    "resistence": "resistance", "sentance": "sentence", "sentances": "sentences",
    "speling": "spelling", "succesful": "successful", "successfull": "successful",
    "suprise": "surprise", "surprize": "surprise", "tendancy": "tendency",
    "therefor": "therefore", "threshhold": "threshold",
    "tommorow": "tomorrow", "tomorow": "tomorrow", "tounge": "tongue",
    "truely": "truly", "unfortunatly": "unfortunately", "unforseen": "unforeseen",
    "wether": "whether", "writting": "writing", "aplication": "application",
    "computr": "computer", "langauge": "language", "mesage": "message",
    "probelm": "problem", "problme": "problem", "exampel": "example",
    "awsome": "awesome", "beter": "better", "pleaes": "please", "plase": "please",
    "thansk": "thanks", "thnaks": "thanks", "welocme": "welcome",
    "yestarday": "yesterday", "yesterady": "yesterday",
    "dont": "don't", "doesnt": "doesn't", "didnt": "didn't", "cant": "can't",
    "wont": "won't", "wouldnt": "wouldn't", "couldnt": "couldn't", "shouldnt": "shouldn't",
    "isnt": "isn't", "arent": "aren't", "wasnt": "wasn't", "werent": "weren't",
    "havent": "haven't", "hasnt": "hasn't", "hadnt": "hadn't",
    "im": "I'm", "ive": "I've", "id": "I'd", "thats": "that's", "whats": "what's",
    "theres": "there's", "heres": "here's", "whos": "who's", "theyre": "they're",
    "youre": "you're", "arent": "aren't",
    "informations": "information", "advices": "advice", "equipments": "equipment",
    "feedbacks": "feedback", "furnitures": "furniture", "homeworks": "homework",
    "knowledges": "knowledge", "softwares": "software", "researches": "research",
    "alot": "a lot", "aswell": "as well", "infact": "in fact", "incase": "in case",
    "eachother": "each other", "nevermind": "never mind", "thankyou": "thank you",
    "goodmorning": "good morning",
}

PROPER_CAPITALIZATION: Dict[str, str] = {
    "i": "I", "i'm": "I'm", "i've": "I've", "i'll": "I'll", "i'd": "I'd",
    "monday": "Monday", "tuesday": "Tuesday", "wednesday": "Wednesday",
    "thursday": "Thursday", "friday": "Friday", "saturday": "Saturday", "sunday": "Sunday",
    "january": "January", "february": "February", "august": "August",
    "september": "September", "october": "October", "november": "November", "december": "December",
    "english": "English", "python": "Python", "c#": "C#", "c++": "C++",
    "windows": "Windows", "grammarly": "Grammarly", "grammerv": "GrammerV",
}

# Words excluded from C++ fuzzy distance-1 spell fallback so common valid English words never collide
SAFE_VALID_WORDS = {
    "went", "want", "make", "made", "take", "took", "give", "gave", "come", "came",
    "know", "knew", "seen", "saw", "done", "does", "said", "says", "tell", "told",
    "find", "found", "left", "keep", "kept", "held", "hold", "bring", "brought",
    "bought", "buying", "think", "thought", "work", "worked", "working", "works",
    "type", "typed", "typing", "types", "word", "words", "reword", "rewording",
    "format", "formats", "formatted", "formatting", "code", "coding", "coded",
    "file", "files", "exe", "dll", "app", "apps", "project", "projects",
    "test", "tests", "tested", "testing", "run", "runs", "running", "ran",
    "just", "like", "show", "shown", "shows", "help", "helps", "helped", "helping",
    "above", "below", "under", "over", "quick", "brown", "fox", "jumps", "jumped",
    "lazy", "dog", "dogs", "cat", "cats", "store", "stores", "apple", "apples",
    "book", "books", "send", "sent", "report", "reports", "check", "checked",
    "stuff", "things", "thing", "issue", "issues", "fix", "fixed", "fixes",
    "document", "documents", "folder", "name", "named", "using", "used", "uses",
}

VOWEL_SOUND_EXCEPTIONS_A = (
    "uni", "use", "usu", "user", "util", "euro", "one-", "once", "ubiqu", "ukulele"
)
SILENT_H_AN = ("hour", "honest", "honor", "honour", "heir")
ACRONYM_AN = {"f", "h", "l", "m", "n", "r", "s", "x"}


def match_case(source: str, target: str) -> str:
    if not source or not target:
        return target
    if target[0].isupper() and source.islower():
        # Proper noun replacement like Grammarly / Monday / I'm
        return target
    if source.isupper() and len(source) > 1:
        return target.upper()
    if source[0].isupper():
        return target[0].upper() + target[1:]
    return target


def needs_an(next_word: str) -> bool:
    clean = re.sub(r"^[^A-Za-z0-9]+|[^A-Za-z0-9-]+$", "", next_word)
    if not clean:
        return False
    low = clean.lower()
    if any(low.startswith(p) for p in SILENT_H_AN):
        return True
    if any(low.startswith(p) for p in VOWEL_SOUND_EXCEPTIONS_A):
        return False
    if clean.isupper() and len(clean) <= 4:
        return low[0] in ACRONYM_AN or low[0] in "aeiou"
    return low[0] in "aeiou"


# Phrase-level grammar rules: (compiled_regex, replacement_or_callable, category, explanation)
GRAMMAR_PHRASE_RULES: List[Tuple[re.Pattern, Any, str, str]] = [
    # Duplicate consecutive words
    (re.compile(r"\b([A-Za-z]+)\s+\1\b", re.IGNORECASE),
     lambda m: m.group(1), "Grammar", "Removed repeated word"),

    # Modal + "of" -> Modal + "have"
    (re.compile(r"\b(could|would|should|must|might)\s+of\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} have", "Grammar", "Use 'have' instead of 'of' after modal verbs"),

    # Past time expressions with "I has went" / "has went" / "have went"
    (re.compile(r"\b(I|you|we|they|he|she)\s+(?:has|have)\s+went\b([^.!?]*?\b(?:yesterday|ago|last\s+\w+)\b)", re.IGNORECASE),
     lambda m: f"{'I' if m.group(1).lower() == 'i' else m.group(1)} went{m.group(2)}",
     "Grammar", "Use simple past 'went' with specific past time expressions"),

    # Perfect tense irregular participles
    (re.compile(r"\b(have|has|had)\s+went\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} gone", "Grammar", "Past participle of 'go' is 'gone'"),
    (re.compile(r"\b(have|has|had)\s+saw\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} seen", "Grammar", "Past participle of 'see' is 'seen'"),
    (re.compile(r"\b(have|has|had)\s+did\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} done", "Grammar", "Past participle of 'do' is 'done'"),
    (re.compile(r"\b(have|has|had)\s+ate\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} eaten", "Grammar", "Past participle of 'eat' is 'eaten'"),
    (re.compile(r"\b(have|has|had)\s+wrote\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} written", "Grammar", "Past participle of 'write' is 'written'"),
    (re.compile(r"\b(have|has|had)\s+took\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} taken", "Grammar", "Past participle of 'take' is 'taken'"),
    (re.compile(r"\b(have|has|had)\s+broke\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} broken", "Grammar", "Past participle of 'break' is 'broken'"),
    (re.compile(r"\b(have|has|had)\s+spoke\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} spoken", "Grammar", "Past participle of 'speak' is 'spoken'"),

    # Subject-verb agreement
    (re.compile(r"\b(I|you|we|they)\s+has\b", re.IGNORECASE),
     lambda m: f"{'I' if m.group(1).lower() == 'i' else m.group(1)} have",
     "Grammar", "Subject-verb agreement: use 'have'"),
    (re.compile(r"\b(he|she|it)\s+have\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} has", "Grammar", "Subject-verb agreement: use 'has'"),
    (re.compile(r"\b(he|she|it)\s+don't\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} doesn't", "Grammar", "Subject-verb agreement: use 'doesn't'"),
    (re.compile(r"\bI\s+is\b", re.IGNORECASE),
     "I am", "Grammar", "Subject-verb agreement: use 'I am'"),
    (re.compile(r"\b(you|we|they)\s+is\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} are", "Grammar", "Subject-verb agreement: use 'are'"),
    (re.compile(r"\b(he|she|it)\s+are\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} is", "Grammar", "Subject-verb agreement: use 'is'"),
    (re.compile(r"\b(you|we|they)\s+was\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} were", "Grammar", "Subject-verb agreement: use 'were'"),

    # Auxiliary do/did/doesn't + wrong verb form
    (re.compile(r"\b(didn't|did\s+not)\s+(went|saw|knew|came|took|made|ate|wrote|gave|ran|bought|thought)\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} " + {
         "went": "go", "saw": "see", "knew": "know", "came": "come", "took": "take",
         "made": "make", "ate": "eat", "wrote": "write", "gave": "give", "ran": "run",
         "bought": "buy", "thought": "think"
     }[m.group(2).lower()],
     "Grammar", "Use base verb form after 'didn't'"),
    (re.compile(r"\b(doesn't|don't|didn't|can't|won't|shouldn't|couldn't|wouldn't)\s+(works|jumps|goes|makes|takes|comes|needs|wants|helps|knows|likes|runs|has)\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} " + (
         "have" if m.group(2).lower() == "has"
         else ("go" if m.group(2).lower() == "goes" else m.group(2).lower()[:-1])
     ),
     "Grammar", "Use base verb form after auxiliary verb"),
    (re.compile(r"\b(the\s+\w+\s+(?:fox|dog|cat|man|woman|boy|girl|user|app|system|car|bird))\s+don't\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} doesn't", "Grammar", "Singular third-person subject requires 'doesn't'"),

    # Double comparatives / superlatives
    (re.compile(r"\bmore\s+(better|easier|faster|stronger|bigger|smaller|simpler|clearer|higher|lower)\b", re.IGNORECASE),
     lambda m: m.group(1), "Grammar", "Avoid double comparative ('more' + '-er')"),
    (re.compile(r"\bmost\s+(best|easiest|fastest|strongest|biggest|smallest|simplest)\b", re.IGNORECASE),
     lambda m: m.group(1), "Grammar", "Avoid double superlative"),

    # Comparative + "then" -> "than"
    (re.compile(r"\b(better|worse|more|less|rather|other|bigger|smaller|faster|slower|easier|harder|higher|lower|greater)\s+then\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} than", "Grammar", "Use 'than' for comparisons instead of 'then'"),

    # Contextual confusables: your/you're, their/there, its/it's
    (re.compile(r"\byour\s+(welcome|right|wrong|going\s+to|doing|amazing|awesome|invited|correct|late)\b", re.IGNORECASE),
     lambda m: f"{match_case(m.group(0)[:4], 'you\'re')} {m.group(1)}",
     "Grammar", "Use contraction 'you're' (you are)"),
    (re.compile(r"\btheir\s+(is|are|was|were)\b", re.IGNORECASE),
     lambda m: f"{match_case(m.group(0)[:5], 'there')} {m.group(1)}",
     "Grammar", "Use 'there' to indicate existence"),
    (re.compile(r"\bits\s+(a|an|very|not|going|been|really|quite|too|so)\b", re.IGNORECASE),
     lambda m: f"{match_case(m.group(0)[:3], 'it\'s')} {m.group(1)}",
     "Grammar", "Use 'it's' for 'it is' or 'it has'"),

    # Natural English phrasing & prepositions
    (re.compile(r"\bwill\s+be\s+shown\s+up\b", re.IGNORECASE),
     "will show up", "Clarity", "'Show up' is active, not passive"),
    (re.compile(r"\btype\s+in\s+anywhere\b", re.IGNORECASE),
     "type anywhere", "Clarity", "Unnecessary preposition 'in' before 'anywhere'"),
    (re.compile(r"\bhelp\s+(me|you|us|them|him|her)\s+to\s+([a-z]+)\b", re.IGNORECASE),
     lambda m: f"help {m.group(1)} {m.group(2)}", "Style", "More natural without 'to' after 'help'"),
    (re.compile(r"\breword\s+or\s+correct\s+or\s+changing\b", re.IGNORECASE),
     "reword, correct, or change", "Grammar", "Use parallel verb forms in a list"),
    (re.compile(r"\bif\s+it\s+required\b", re.IGNORECASE),
     "if required", "Grammar", "Missing verb 'is' or simplify to 'if required'"),
    (re.compile(r"\b(went|go|going|came|come)\s+to\s+([^,.!?]+?)\s+for\s+(buying|getting|making|finding|seeing|taking|checking)\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} to {m.group(2)} to " + {
         "buying": "buy", "getting": "get", "making": "make", "finding": "find",
         "seeing": "see", "taking": "take", "checking": "check"
     }[m.group(3).lower()],
     "Grammar", "Use infinitive 'to + verb' to express purpose"),
    (re.compile(r"\b(many|several|few|two|three|four|five|some)\s+(apple|mistake|error|reason|sentence|word|thing|item|problem|issue|friend|book)\b", re.IGNORECASE),
     lambda m: None if m.group(1).lower() == "some" and m.group(2).lower() in ("reason",) else f"{m.group(1)} {m.group(2)}s",
     "Grammar", "Plural quantifier requires a plural countable noun"),
    (re.compile(r"\bone\s+of\s+the\s+(?:\w+\s+)?(reason|thing|problem|issue|mistake|feature|way|friend|person)\b", re.IGNORECASE),
     lambda m: (m.group(0)[:-len(m.group(1))] + ("people" if m.group(1).lower() == "person" else m.group(1) + "s")),
     "Grammar", "'One of the' is followed by a plural noun"),

    # Passive error on intransitive verbs ("will be remained" -> "will remain", "was happened" -> "happened")
    (re.compile(r"\bwill\s+be\s+(remained|happened|occurred|appeared|disappeared|existed|seemed)\b", re.IGNORECASE),
     lambda m: "will " + {
         "remained": "remain", "happened": "happen", "occurred": "occur",
         "appeared": "appear", "disappeared": "disappear", "existed": "exist", "seemed": "seem"
     }[m.group(1).lower()],
     "Grammar", "Intransitive verbs like 'remain' or 'happen' do not use passive voice"),
    (re.compile(r"\b(?:is|are|was|were)\s+(remained|happened|occurred|disappeared)\b", re.IGNORECASE),
     lambda m: ("remain" if m.group(0).lower().startswith("are") else m.group(1).lower()),
     "Grammar", "Intransitive verb should be active, not passive"),

    # Third-person singular present tense agreement ("when it delete" -> "when it deletes", "he want" -> "he wants")
    (re.compile(r"(?<!\bdoes\s)(?<!\bdid\s)(?<!\bwill\s)(?<!\bcan\s)(?<!\bcould\s)(?<!\bwould\s)(?<!\bshould\s)(?<!\blet\s)(?<!\bmake\s)\b(he|she|it)\s+(delete|work|make|take|want|need|seem|look|help|give|come|go|try|use|show|fail|break|run|keep|remain|write|read|fix|change)\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} " + (
         "goes" if m.group(2).lower() == "go"
         else ("tries" if m.group(2).lower() == "try"
               else ("fixes" if m.group(2).lower() == "fix" else m.group(2).lower() + "s"))
     ),
     "Grammar", "Third-person singular subject ('he/she/it') requires '-s' verb ending"),

    # Plural subject with singular -s verb ("words remains" -> "words remain", "they wants" -> "they want")
    (re.compile(r"\b(they|we|you|words|people|things|items|users|browsers|sentences)\s+(deletes|works|makes|takes|wants|needs|seems|looks|helps|goes|runs|remains|shows|fails)\b", re.IGNORECASE),
     lambda m: f"{m.group(1)} " + ("go" if m.group(2).lower() == "goes" else m.group(2).lower()[:-1]),
     "Grammar", "Plural subject takes base plural verb form"),

    # Common collocation & redundancy fixes
    (re.compile(r"\bcan\s+not\b", re.IGNORECASE),
     "cannot", "Style", "'Cannot' is standardly written as one word"),
    (re.compile(r"\bdiscuss\s+about\b", re.IGNORECASE),
     "discuss", "Grammar", "'Discuss' does not take 'about'"),
    (re.compile(r"\bexplain\s+(me|him|her|us|them)\b", re.IGNORECASE),
     lambda m: f"explain to {m.group(1)}", "Grammar", "Use 'explain to <person>'"),
    (re.compile(r"\b(reply|return|revert)\s+back\b", re.IGNORECASE),
     lambda m: m.group(1), "Clarity", "Removed redundant 'back'"),
    (re.compile(r"\brepeat\s+again\b", re.IGNORECASE),
     "repeat", "Clarity", "Removed redundant 'again'"),
]


def apply_split_word_joins(text: str, issues: List[Dict[str, str]]) -> str:
    # Detect accidentally space-split words like "chec k" -> "check", "gramm ar" -> "grammar"
    if not CPP_BRIDGE.available or not getattr(CPP_BRIDGE, "has_valid_word_export", False):
        return text

    split_re = re.compile(r"\b([A-Za-z]{2,15}) ([A-Za-z]{1,10})\b")

    def _try_join(m: re.Match) -> str:
        w1, w2 = m.group(1), m.group(2)
        low1, low2 = w1.lower(), w2.lower()
        # Never merge if first fragment is already a valid English word or known standalone typo
        if low1 in COMMON_MISSPELLINGS or CPP_BRIDGE.is_valid_word(w1):
            return m.group(0)
        # Second fragment must either be a stray single letter (not 'a' or 'I') or also an invalid fragment
        w2_is_stray_char = (len(w2) == 1 and low2 not in ("a", "i"))
        if not w2_is_stray_char and (low2 in COMMON_MISSPELLINGS or CPP_BRIDGE.is_valid_word(w2)):
            return m.group(0)
        merged = w1 + w2.lower() if not w1.isupper() else (w1 + w2)
        if CPP_BRIDGE.is_valid_word(merged):
            issues.append({
                "original": f"{w1} {w2}",
                "replacement": merged,
                "category": "Spelling",
                "explanation": f"Merged accidentally split word '{merged}'",
            })
            return merged
        return m.group(0)

    return split_re.sub(_try_join, text)


def apply_word_spelling_and_caps(text: str, issues: List[Dict[str, str]]) -> str:
    text = apply_split_word_joins(text, issues)
    # Fix token-level spelling & proper capitalization while preserving whitespace/punctuation
    token_re = re.compile(r"[A-Za-z][A-Za-z'+#]*")

    def repl(match: re.Match) -> str:
        word = match.group(0)
        low = word.lower()

        # Skip contractions already properly formed unless in capitalization map
        if low in PROPER_CAPITALIZATION:
            fixed = PROPER_CAPITALIZATION[low]
            if fixed != word:
                issues.append({
                    "original": word,
                    "replacement": fixed,
                    "category": "Capitalization" if low == fixed.lower() else "Grammar",
                    "explanation": f"Capitalize '{fixed}' properly",
                })
                return fixed
            return word

        if low in COMMON_MISSPELLINGS:
            target = COMMON_MISSPELLINGS[low]
            fixed = match_case(word, target)
            if fixed != word:
                cat = "Grammar" if "'" in fixed else "Spelling"
                issues.append({
                    "original": word,
                    "replacement": fixed,
                    "category": cat,
                    "explanation": f"Did you mean '{fixed}'?",
                })
                return fixed
            return word

        # Native C++ Damerau-Levenshtein fallback for unknown words
        if "'" not in word and "+" not in word and "#" not in word and low not in SAFE_VALID_WORDS:
            cpp_cand = CPP_BRIDGE.suggest(word)
            if cpp_cand and cpp_cand != word:
                issues.append({
                    "original": word,
                    "replacement": cpp_cand,
                    "category": "Spelling",
                    "explanation": f"Spelling correction via C++ Damerau-Levenshtein ('{cpp_cand}')",
                })
                return cpp_cand

        return word

    return token_re.sub(repl, text)


def apply_article_rules(text: str, issues: List[Dict[str, str]]) -> str:
    art_re = re.compile(r"\b(a|an)\s+([A-Za-z][A-Za-z0-9-]*)\b", re.IGNORECASE)

    def repl(match: re.Match) -> str:
        art = match.group(1)
        nxt = match.group(2)
        want_an = needs_an(nxt)
        desired = "an" if want_an else "a"
        desired_cased = match_case(art, desired)
        if desired_cased != art:
            issues.append({
                "original": f"{art} {nxt}",
                "replacement": f"{desired_cased} {nxt}",
                "category": "Grammar",
                "explanation": f"Use '{desired_cased}' before '{nxt}' based on initial sound",
            })
            return f"{desired_cased} {nxt}"
        return match.group(0)

    return art_re.sub(repl, text)


def apply_punctuation_and_sentence_caps(text: str, issues: List[Dict[str, str]]) -> str:
    orig = text
    # Normalize spaces around commas/periods/question/exclamation marks
    s = re.sub(r"[ \t]+([,.;:!?])", r"\1", text)
    s = re.sub(r"([,;:])([A-Za-z])", r"\1 \2", s)
    s = re.sub(r"([.!?])([A-Za-z])", r"\1 \2", s)
    s = re.sub(r"[ \t]{2,}", " ", s)
    if s != orig:
        issues.append({
            "original": "spacing/punctuation",
            "replacement": "clean spacing",
            "category": "Punctuation",
            "explanation": "Normalized spacing around punctuation",
        })

    # Capitalize start of text and start of sentences
    def cap_start(m: re.Match) -> str:
        prefix = m.group(1)
        ch = m.group(2)
        if ch.islower():
            issues.append({
                "original": ch,
                "replacement": ch.upper(),
                "category": "Capitalization",
                "explanation": "Capitalize the first letter of a sentence",
            })
            return prefix + ch.upper()
        return m.group(0)

    s = re.sub(r"^(\s*)([a-z])", cap_start, s)
    s = re.sub(r"([.!?]\s+)([a-z])", cap_start, s)

    # Add ending question mark or period when clearly a full sentence (>= 6 words) lacking terminal punctuation,
    # OR convert trailing period '.' to '?' when clearly a direct question (e.g. "Ok, what do you mean... .")
    stripped = s.rstrip()
    words = stripped.split()
    direct_q_re = re.compile(
        r"^\s*(?:(?:ok|okay|hey|hi|hello|well|so|please)[,\s]+)?(?:(?:what|why|how|when|where|who)\s+(?:do|does|did|is|are|was|were|can|could|will|would|should)|(?:can|could|would|will|should|do|does|did|is|are)\s+(?:i|you|we|they|he|she|it))\b",
        re.IGNORECASE,
    )
    if stripped and stripped.endswith(".") and direct_q_re.search(stripped):
        s = stripped[:-1] + "?"
        issues.append({
            "original": words[-1],
            "replacement": words[-1][:-1] + "?",
            "category": "Punctuation",
            "explanation": "Direct questions should end with a question mark '?'",
        })
    elif len(words) >= 6 and stripped and stripped[-1] not in ".!?:;,)":
        first_low = re.sub(r"[^a-z]", "", words[0].lower())
        is_question = bool(direct_q_re.search(stripped)) or first_low in {
            "can", "could", "would", "will", "should", "do", "does", "did",
            "is", "are", "was", "were", "what", "why", "how", "when", "where", "who"
        }
        term = "?" if is_question else "."
        s = stripped + term
        issues.append({
            "original": words[-1],
            "replacement": words[-1] + term,
            "category": "Punctuation",
            "explanation": f"Added ending '{term}' to complete the sentence",
        })

    return s


def build_word_diff(original: str, corrected: str) -> List[Dict[str, str]]:
    orig_tokens = re.findall(r"\S+|\s+", original)
    corr_tokens = re.findall(r"\S+|\s+", corrected)
    matcher = difflib.SequenceMatcher(a=orig_tokens, b=corr_tokens)
    out: List[Dict[str, str]] = []
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == "equal":
            out.append({"op": "eq", "text": "".join(orig_tokens[i1:i2])})
        elif tag == "delete":
            out.append({"op": "del", "text": "".join(orig_tokens[i1:i2])})
        elif tag == "insert":
            out.append({"op": "ins", "text": "".join(corr_tokens[j1:j2])})
        elif tag == "replace":
            out.append({"op": "del", "text": "".join(orig_tokens[i1:i2])})
            out.append({"op": "ins", "text": "".join(corr_tokens[j1:j2])})
    return out


# ---------------------------------------------------------------------------
# Persian (فارسی) & Finglish (فینگلیش → فارسی) Linguistic Engine
# ---------------------------------------------------------------------------
PERSIAN_SCRIPT_RE = re.compile(r"[\u0600-\u06FF\uFB50-\uFDFF\uFE70-\uFEFF]")
ZWNJ = "\u200C"

# Multi-word conversational Finglish expressions
FINGLISH_PHRASES: List[Tuple[re.Pattern, str]] = [
    (re.compile(r"\bsalam\s+aleykom\b", re.IGNORECASE), "سلام علیکم"),
    (re.compile(r"\bhalet\s+chetore\b", re.IGNORECASE), "حالت چطوره"),
    (re.compile(r"\bhalet\s+chetori\b", re.IGNORECASE), "حالت چطوره"),
    (re.compile(r"\bhaletoon\s+chetore\b", re.IGNORECASE), "حالتون چطوره"),
    (re.compile(r"\bhale\s+shoma\s+chetore\b", re.IGNORECASE), "حال شما چطوره"),
    (re.compile(r"\bhale\s+shoma\s+chetor\s+ast\b", re.IGNORECASE), "حال شما چطور است"),
    (re.compile(r"\bche\s+khabar\b", re.IGNORECASE), "چه خبر"),
    (re.compile(r"\bche\s+khabara\b", re.IGNORECASE), "چه خبرا"),
    (re.compile(r"\bkhaste\s+nabashi\b", re.IGNORECASE), "خسته نباشی"),
    (re.compile(r"\bkhaste\s+nabashid\b", re.IGNORECASE), "خسته نباشید"),
    (re.compile(r"\bdastet\s+dard\s+nakone\b", re.IGNORECASE), "دستت درد نکنه"),
    (re.compile(r"\bdastetoon\s+dard\s+nakone\b", re.IGNORECASE), "دستتون درد نکنه"),
    (re.compile(r"\bdamet\s+garm\b", re.IGNORECASE), "دمت گرم"),
    (re.compile(r"\bsobh\s+bekheir\b", re.IGNORECASE), "صبح بخیر"),
    (re.compile(r"\brooz\s+bekheir\b", re.IGNORECASE), "روز بخیر"),
    (re.compile(r"\bshab\s+bekheir\b", re.IGNORECASE), "شب بخیر"),
    (re.compile(r"\basr\s+bekheir\b", re.IGNORECASE), "عصر بخیر"),
    (re.compile(r"\btavalodet\s+mobarak\b", re.IGNORECASE), "تولدت مبارک"),
    (re.compile(r"\bsale\s+no\s+mobarak\b", re.IGNORECASE), "سال نو مبارک"),
    (re.compile(r"\beid\s+mobarak\b", re.IGNORECASE), "عید مبارک"),
    (re.compile(r"\bbe\s+omide\s+didar\b", re.IGNORECASE), "به امید دیدار"),
    (re.compile(r"\bkhahesh\s+mikonam\b", re.IGNORECASE), "خواهش می‌کنم"),
    (re.compile(r"\bmamnoon\s+misham\b", re.IGNORECASE), "ممنون می‌شم"),
    (re.compile(r"\benshallah\b|\binshallah\b", re.IGNORECASE), "ان‌شاءالله"),
]

# Unambiguous Finglish words (do NOT collide with common everyday English words)
STRONG_FINGLISH_LEXICON: Dict[str, str] = {
    # Greetings & politeness
    "salam": "سلام", "slm": "سلام", "salaam": "سلام", "dorood": "درود", "dorud": "درود",
    "khodahafez": "خداحافظ", "khodafez": "خداحافظ", "bedrood": "بدرود", "felan": "فعلاً",
    "mersi": "مرسی", "mamnoon": "ممنون", "mamnun": "ممنون", "mamnoonam": "ممنونم", "mamnunam": "ممنونم",
    "motshakeram": "متشکرم", "moteshakeram": "متشکرم", "tashakor": "تشکر", "sepas": "سپاس",
    "sepasgozaram": "سپاسگزارم", "lotfan": "لطفاً", "khahesh": "خواهش", "bebakhshid": "ببخشید",
    "sharmande": "شرمنده", "mobarak": "مبارک", "afarin": "آفرین",
    # Well-being & questions
    "halet": "حالت", "haletoon": "حالتون", "halam": "حالم", "halesh": "حالش",
    "chetor": "چطور", "chetore": "چطوره", "chetori": "چطوری", "chetorid": "چطورید", "chetorin": "چطورین",
    "khoob": "خوب", "khob": "خوب", "khoobi": "خوبی", "khobi": "خوبی",
    "khoobam": "خوبم", "khobam": "خوبم", "khoobe": "خوبه", "khobe": "خوبه", "khoobim": "خوبیم",
    "che": "چه", "chi": "چی", "chie": "چیه", "chiye": "چیه", "chist": "چیست",
    "koja": "کجا", "kojast": "کجاست", "kojayi": "کجایی", "kojaee": "کجایی",
    "chera": "چرا", "chand": "چند", "kodoom": "کدوم", "kodam": "کدام", "chegoone": "چگونه",
    "aya": "آیا", "ki": "کی", "kist": "کیست",
    # Pronouns & people
    "shoma": "شما", "ma": "ما", "anha": "آن‌ها", "oona": "اونا", "ina": "اینا", "inha": "این‌ها",
    "khodam": "خودم", "khodet": "خودت", "khodesh": "خودش",
    "doost": "دوست", "dust": "دوست", "doostam": "دوستم", "azizam": "عزیزم", "aziz": "عزیز",
    "janam": "جانم", "joonam": "جونم", "refigh": "رفیق", "pedar": "پدر", "madar": "مادر",
    "baradar": "برادر", "khahar": "خواهر", "pesar": "پسر", "dokhtar": "دختر", "bache": "بچه",
    # Common verbs (spoken & formal)
    "hastam": "هستم", "hasti": "هستی", "hast": "هست", "hastim": "هستیم", "hastid": "هستید", "hastand": "هستند",
    "nistam": "نیستم", "nisti": "نیستی", "nist": "نیست", "boodam": "بودم", "bood": "بود", "bud": "بود",
    "daram": "دارم", "dari": "داری", "dare": "داره", "darad": "دارد", "darim": "داریم", "darid": "دارید",
    "nadaram": "ندارم", "nadari": "نداری", "nadare": "نداره", "nadarad": "ندارد",
    "mikonam": "می‌کنم", "mikoni": "می‌کنی", "mikone": "می‌کنه", "mikonad": "می‌کند", "mikonim": "می‌کنیم", "mikonid": "می‌کنید",
    "nemikonam": "نمی‌کنم", "nemikone": "نمی‌کنه", "bekon": "بکن", "nakon": "نکن",
    "mikham": "می‌خوام", "mikhay": "می‌خوای", "mikhad": "می‌خواد", "mikhaham": "می‌خواهم", "mikhahad": "می‌خواهد",
    "nemikham": "نمی‌خوام", "nemikhay": "نمی‌خوای", "nemikhad": "نمی‌خواد",
    "miram": "می‌رم", "miri": "می‌ری", "mire": "می‌ره", "mirim": "می‌ریم", "miravam": "می‌روم", "miravad": "می‌رود",
    "raftam": "رفتم", "rafti": "رفتی", "raft": "رفت", "raftim": "رفتیم", "boro": "برو", "berim": "بریم", "naro": "نرو",
    "miyam": "میام", "miyay": "میای", "miyad": "میاد", "miayam": "می‌آیم", "miayad": "می‌آید",
    "omadam": "اومدم", "oomadam": "اومدم", "amadam": "آمدم", "amad": "آمد", "oomad": "اومد", "bia": "بیا", "biayid": "بیایید",
    "midonam": "می‌دونم", "midunam": "می‌دونم", "midoni": "می‌دونی", "miduni": "می‌دونی", "midanam": "می‌دانم",
    "nemidonam": "نمی‌دونم", "nemidunam": "نمی‌دونم", "nemidanam": "نمی‌دانم",
    "mitoonam": "می‌تونم", "mitunam": "می‌تونم", "mitooni": "می‌تونی", "mituni": "می‌تونی", "mitavanam": "می‌توانم",
    "nemitoonam": "نمی‌تونم", "nemitunam": "نمی‌تونم", "nemitavanam": "نمی‌توانم",
    "migam": "می‌گم", "migi": "می‌گی", "mige": "می‌گه", "migooyam": "می‌گویم", "migooyad": "می‌گوید",
    "goftam": "گفتم", "gofti": "گفتی", "goft": "گفت", "begoo": "بگو", "begu": "بگو", "nagoo": "نگو",
    "mibinam": "می‌بینم", "mibini": "می‌بینی", "mibine": "می‌بینه", "didam": "دیدم", "didi": "دیدی", "bebin": "ببین",
    "midam": "می‌دم", "midi": "می‌دی", "mide": "می‌ده", "dadam": "دادم", "bede": "بده", "bedid": "بدید",
    "migiram": "می‌گیرم", "migiri": "می‌گیری", "gereftam": "گرفتم", "begir": "بگیر",
    "mikhoonam": "می‌خونم", "mikhunam": "می‌خونم", "mikhanam": "می‌خوانم", "bekhoon": "بخون", "bekhun": "بخون",
    "minevisam": "می‌نویسم", "neveshtam": "نوشتم", "benevis": "بنویس",
    "misham": "می‌شم", "mishi": "می‌شی", "mishe": "می‌شه", "mishavad": "می‌شود", "nemishe": "نمی‌شه",
    "shod": "شد", "shodam": "شدم", "beshe": "بشه", "bashe": "باشه", "bash": "باش",
    "kardam": "کردم", "kardi": "کردی", "kard": "کرد", "kardim": "کردیم",
    "bayad": "باید", "nabayad": "نباید", "shayad": "شاید", "hatman": "حتماً", "aslan": "اصلاً", "vaghean": "واقعاً",
    # Time, places & everyday nouns/adjectives
    "emrooz": "امروز", "emruz": "امروز", "farda": "فردا", "dirooz": "دیروز", "diruz": "دیروز",
    "alan": "الان", "hala": "حالا", "sobh": "صبح", "zohr": "ظهر", "asr": "عصر", "shab": "شب",
    "hafte": "هفته", "mah": "ماه", "sal": "سال", "saat": "ساعت", "daghighe": "دقیقه",
    "khoone": "خونه", "khune": "خونه", "khane": "خانه", "daneshgah": "دانشگاه", "madrese": "مدرسه",
    "sherkat": "شرکت", "otagh": "اتاق", "shahr": "شهر", "keshvar": "کشور", "tehran": "تهران",
    "ketab": "کتاب", "barname": "برنامه", "komak": "کمک", "soal": "سؤال", "javab": "جواب", "jawab": "جواب",
    "payam": "پیام", "matn": "متن", "jomle": "جمله", "kalame": "کلمه", "farsi": "فارسی", "finglish": "فینگلیش",
    "moshkel": "مشکل", "masale": "مسئله", "gozaresh": "گزارش", "jalase": "جلسه", "prozhe": "پروژه",
    "dorost": "درست", "ghalat": "غلط", "eshtebah": "اشتباه", "kheili": "خیلی", "kheyli": "خیلی",
    "ziad": "زیاد", "bishtar": "بیشتر", "behtar": "بهتر", "behtarin": "بهترین",
    "bozorg": "بزرگ", "koochik": "کوچیک", "kuchik": "کوچیک", "koochak": "کوچک",
    "ghashang": "قشنگ", "ziba": "زیبا", "aali": "عالی", "aliye": "عالیه",
    "jadid": "جدید", "ghadimi": "قدیمی", "sari": "سریع", "rahat": "راحت", "sakht": "سخت",
    "bale": "بله", "are": "آره", "areh": "آره", "nakheir": "نخیر", "na": "نه", "chashm": "چشم",
    "khabar": "خبر", "zendegi": "زندگی", "donya": "دنیا", "delam": "دلم", " tang ": " تنگ ",
    "baraye": "برای", "vase": "واسه", "chon": "چون", "vali": "ولی", "agar": "اگر", "age": "اگه",
    "vaghti": "وقتی", "hame": "همه", "hich": "هیچ", "chizi": "چیزی", "kasi": "کسی",
}

# Context-dependent Finglish words whose Latin spelling also appears in English
WEAK_FINGLISH_LEXICON: Dict[str, str] = {
    "man": "من", "to": "تو", "oo": "او", "ou": "او",
    "be": "به", "ba": "با", "az": "از", "dar": "در", "bar": "بر",
    "in": "این", "oon": "اون", "on": "اون", "an": "آن",
    "va": "و", "o": "و", "ke": "که", "ta": "تا", "ya": "یا", "ama": "اما",
    "ham": "هم", "kar": "کار", "sar": "سر", "bad": "بعد", "kam": "کم",
    "nam": "نام", "rah": "راه", "iran": "ایران", "ali": "عالی",
}

PERSIAN_QUESTION_WORDS = {
    "آیا", "چطور", "چطوره", "چطوری", "چطورید", "چطورین", "چگونه",
    "چرا", "کجا", "کجاست", "کجایی", "کیست", "چیست", "چیه", "چی",
    "کدام", "کدوم", "چندتا",
}


def _is_persian_question_ending(s: str) -> bool:
    """True when a Persian sentence/final clause is genuinely interrogative (avoids substrings like چراغ)."""
    tokens = re.findall(r"[آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهیئؤإأ\u200C]+", s)
    if not tokens:
        return False
    tail = tokens[-7:] if len(tokens) > 7 else tokens
    if any(t in PERSIAN_QUESTION_WORDS for t in tail):
        return True
    if len(tokens) <= 4 and tokens[-1] in ("خوبی", "خوبید", "خوبین", "درسته", "موافقید", "موافقی", "هستی", "هستید"):
        return True
    return False

# Frequent Persian misspellings, homophone confusions, colloquial-orthography & tanwin fixes
PERSIAN_MISSPELLINGS: Dict[str, str] = {
    "صلام": "سلام", "سلان": "سلام", "مرصی": "مرسی", "خاهش": "خواهش", "خاهشمندم": "خواهشمندم",
    "خاستن": "خواستن", "خاستم": "خواستم", "خاست": "خواست", "خاهر": "خواهر", "خاهرم": "خواهرم",
    "خاب": "خواب", "خابیدن": "خوابیدن", "خابیدم": "خوابیدم",
    "سپاسگذار": "سپاسگزار", "سپاسگذارم": "سپاسگزارم", "سپاسگذاری": "سپاسگزاری",
    "گذارش": "گزارش", "برگذار": "برگزار", "برگذاری": "برگزاری",
    "حوضور": "حضور", "منزور": "منظور", "انتضار": "انتظار", "انتضامات": "انتظامات",
    "نضم": "نظم", "ناضم": "ناظم", "اضهار": "اظهار", "خداحافض": "خداحافظ", "حافض": "حافظ",
    "اطاق": "اتاق", "باطری": "باتری", "طپش": "تپش", "امپراطور": "امپراتور", "امپراطوری": "امپراتوری",
    "طوطئه": "توطئه", "پزشگ": "پزشک", "توجیح": "توجیه", "ترجیه": "ترجیح",
    "مسعله": "مسئله", "مسله": "مسئله", "مطمعن": "مطمئن", "مطمعنا": "مطمئناً",
    "متاسفانه": "متأسفانه", "متاسفم": "متأسفم", "تاسف": "تأسف",
    "تاثیر": "تأثیر", "تایید": "تأیید", "تاکید": "تأکید", "تاخیر": "تأخیر", "تاسیس": "تأسیس",
    "مسعول": "مسئول", "مسول": "مسئول", "مسولیت": "مسئولیت", "سوال": "سؤال",
    "انشالله": "ان‌شاءالله", "انشاالله": "ان‌شاءالله",
    "پرفسور": "پروفسور", "تلفون": "تلفن", "ملیون": "میلیون", "ملیارد": "میلیارد", "انشتین": "اینشتین",
    "خواستگاه": "خاستگاه", "خاستار": "خواستار", "سپاس گذارم": "سپاسگزارم",
    "بنیانگذار": "بنیان‌گذار", "قانونگذار": "قانون‌گذار", "سرمایه گذاری": "سرمایه‌گذاری",
    "هیچوقت": "هیچ‌وقت", "هیچکس": "هیچ‌کس", "هیچکدام": "هیچ‌کدام", "همینطور": "همین‌طور", "همانطور": "همان‌طور",
    "چطوروری": "چطوری", "چطوریی": "چطوری", "سلاام": "سلام", "سلااام": "سلام",
    "ممنوون": "ممنون", "خوووب": "خوب", "خووب": "خوب",
    "غلطها": "غلط‌ها", "کارها": "کارها", "کتابها": "کتاب‌ها", "برنامهها": "برنامه‌ها", "پیامها": "پیام‌ها",
    "واژهها": "واژه‌ها", "جملهها": "جمله‌ها", "انسانها": "انسان‌ها", "دوستها": "دوست‌ها", "خانهها": "خانه‌ها",
    "بزرگتر": "بزرگ‌تر", "بزرگترین": "بزرگ‌ترین", "کوچکتر": "کوچک‌تر", "کوچکترین": "کوچک‌ترین",
    "سریعتر": "سریع‌تر", "راحتتر": "راحت‌تر", "زیباتر": "زیبا\u200cتر", "زیباترین": "زیبا\u200cترین",
    "مهمتر": "مهم‌تر", "مهمترین": "مهم‌ترین", "جدیدتر": "جدید\u200cتر", "جدیدترین": "جدید\u200cترین",
    # Tanwin standard orthography
    "لطفا": "لطفاً", "حتما": "حتماً", "اصلا": "اصلاً", "مثلا": "مثلاً",
    "کاملا": "کاملاً", "دقیقا": "دقیقاً", "تقریبا": "تقریباً", "معمولا": "معمولاً",
    "احتمالا": "احتمالاً", "واقعا": "واقعاً", "مخصوصا": "مخصوصاً", "نسبتا": "نسبتاً",
    "شخصا": "شخصاً", "ظاهرا": "ظاهراً", "قبلا": "قبلاً", "بعدا": "بعداً",
    "فعلا": "فعلاً", "مجددا": "مجدداً", "ابدا": "ابداً", "صرفا": "صرفاً",
    "قطعا": "قطعاً", "سریعا": "سریعاً", "مستقیما": "مستقیماً", "ترجیحا": "ترجیحاً",
    # Common fused می / نمی verbs missing ZWNJ
    "میروم": "می‌روم", "میرود": "می‌رود", "میرویم": "می‌رویم", "میروند": "می‌روند",
    "میرم": "می‌رم", "میری": "می‌ری", "میره": "می‌ره", "میریم": "می‌ریم", "میرن": "می‌رن",
    "میکنم": "می‌کنم", "میکنی": "می‌کنی", "میکند": "می‌کند", "میکنه": "می‌کنه",
    "میکنیم": "می‌کنیم", "میکنید": "می‌کنید", "میکنند": "می‌کنند",
    "نمیکنم": "نمی‌کنم", "نمیکند": "نمی‌کند", "نمیکنه": "نمی‌کنه",
    "میشود": "می‌شود", "میشوم": "می‌شوم", "میشوی": "می‌شوی", "میشه": "می‌شه",
    "نمیشود": "نمی‌شود", "نمیشه": "نمی‌شه",
    "میخواهم": "می‌خواهم", "میخواهی": "می‌خواهی", "میخواهد": "می‌خواهد", "میخواهیم": "می‌خواهیم",
    "میخوام": "می‌خوام", "میخوای": "می‌خوای", "میخواد": "می‌خواد", "میخاهم": "می‌خواهم", "میخام": "می‌خوام",
    "نمیدانم": "نمی‌دانم", "نمیدونم": "نمی‌دونم", "میدانم": "می‌دانم", "میدونم": "می‌دونم",
    "میدانی": "می‌دانی", "میدونی": "می‌دونی", "میداند": "می‌داند",
    "میتوانم": "می‌توانم", "میتواند": "می‌تواند", "میتوان": "می‌توان", "میتوانیم": "می‌توانیم",
    "میتونم": "می‌تونم", "میتونی": "می‌تونی", "میتونه": "می‌تونه",
    "نمیتوانم": "نمی‌توانم", "نمیتونم": "نمی‌تونم", "نمیتوان": "نمی‌توان",
    "میگویم": "می‌گویم", "میگوید": "می‌گوید", "میگم": "می‌گم", "میگی": "می‌گی", "میگه": "می‌گه",
    "میآیم": "می‌آیم", "میآید": "می‌آید", "میبینم": "می‌بینم", "میبینی": "می‌بینی", "میبیند": "می‌بیند",
    "میدهم": "می‌دهم", "میدهد": "می‌دهد", "میگیرم": "می‌گیرم", "میگیرد": "می‌گیرد",
    "مینویسم": "می‌نویسم", "مینویسد": "می‌نویسد", "میخوانم": "می‌خوانم", "میخواند": "می‌خواند", "میخونم": "می‌خونم",
}

PERSIAN_NON_NOUN_BEFORE_HA = {
    "از", "به", "با", "در", "بر", "برای", "تا", "یا", "که", "و", "چون", "اگر", "مگر", "ولی", "اما"
}


def detect_language_mode(text: str) -> str:
    """Return 'fa' for Persian script, 'finglish' for Latin-script Persian, or 'en' for English."""
    if not text or not text.strip():
        return "en"
    if PERSIAN_SCRIPT_RE.search(text):
        return "fa"

    for pat, _ in FINGLISH_PHRASES:
        if pat.search(text):
            return "finglish"

    words = re.findall(r"[A-Za-z']+", text.lower())
    if not words:
        return "en"

    strong_hits = 0
    weak_hits = 0
    for w in words:
        if w in STRONG_FINGLISH_LEXICON:
            strong_hits += 1
            continue
        # Check productive Finglish suffixes on strong stems (e.g. ketabam, doostet, khoonam)
        matched_stem = False
        for suf in ("emoon", "etoon", "eshoon", "am", "et", "esh", "ha", "tar", "tarin"):
            if len(w) > len(suf) + 2 and w.endswith(suf) and w[:-len(suf)] in STRONG_FINGLISH_LEXICON:
                strong_hits += 1
                matched_stem = True
                break
        if matched_stem:
            continue
        # Characteristic Finglish digraphs (kh, gh, zh) on words NOT valid in English dictionary
        if len(w) >= 4 and any(dg in w for dg in ("kh", "gh", "zh")) and not CPP_BRIDGE.is_valid_word(w):
            strong_hits += 1
            continue
        if w in WEAK_FINGLISH_LEXICON:
            weak_hits += 1

    if len(words) == 1:
        return "finglish" if strong_hits == 1 else "en"

    ratio = (strong_hits + weak_hits) / float(len(words))
    if (strong_hits >= 1 and ratio >= 0.45) or (strong_hits >= 2 and ratio >= 0.34):
        return "finglish"
    return "en"


def _phonetic_finglish_to_fa(word: str) -> str:
    """Rule-based phonetic transliterator for out-of-vocabulary Finglish words."""
    w = word.lower().strip("'")
    if not w:
        return word

    # Check stem + suffix decomposition first
    suffix_map = [
        ("emoon", "مون"), ("etoon", "تون"), ("eshoon", "شون"),
        ("hayam", ZWNJ + "هایم"), ("hayat", ZWNJ + "هایت"), ("hayash", ZWNJ + "هایش"),
        ("haye", ZWNJ + "های"), ("ha", ZWNJ + "ها"),
        ("tarin", ZWNJ + "ترین"), ("tar", ZWNJ + "تر"),
        ("am", "م"), ("et", "ت"), ("at", "ت"), ("esh", "ش"), ("ash", "ش"),
    ]
    for lat_suf, fa_suf in suffix_map:
        if len(w) > len(lat_suf) + 2 and w.endswith(lat_suf):
            stem = w[:-len(lat_suf)]
            if stem in STRONG_FINGLISH_LEXICON:
                return STRONG_FINGLISH_LEXICON[stem] + fa_suf
            if stem in WEAK_FINGLISH_LEXICON:
                return WEAK_FINGLISH_LEXICON[stem] + fa_suf

    out: List[str] = []
    i = 0
    n = len(w)
    while i < n:
        two = w[i:i + 2]
        if two == "kh":
            out.append("خ"); i += 2; continue
        if two == "gh":
            out.append("ق"); i += 2; continue
        if two == "sh":
            out.append("ش"); i += 2; continue
        if two == "ch":
            out.append("چ"); i += 2; continue
        if two == "zh":
            out.append("ژ"); i += 2; continue
        if two in ("oo", "ou"):
            out.append("او" if i == 0 else "و"); i += 2; continue
        if two == "aa":
            out.append("آ" if i == 0 else "ا"); i += 2; continue
        if two in ("ee", "ei", "ey"):
            out.append("ای" if i == 0 else "ی"); i += 2; continue
        if two == "eh" and i + 2 == n:
            out.append("ه"); i += 2; continue

        ch = w[i]
        if i == 0 and ch in "aeiou":
            if ch == "a":
                out.append("ا")
            elif ch in ("o", "u"):
                out.append("او")
            elif ch == "i":
                out.append("ای")
            else:
                out.append("ا")
            i += 1
            continue

        if i == n - 1 and ch == "e":
            out.append("ه"); i += 1; continue

        char_map = {
            "a": "ا", "b": "ب", "p": "پ", "t": "ت", "s": "س", "j": "ج",
            "h": "ه", "d": "د", "r": "ر", "z": "ز", "f": "ف", "q": "ق",
            "k": "ک", "g": "گ", "l": "ل", "m": "م", "n": "ن", "v": "و",
            "w": "و", "u": "و", "y": "ی", "i": "ی", "c": "ک", "x": "کس",
        }
        # Short medial 'e' and 'o' are unwritten harakat in Persian orthography
        if ch in ("e", "o"):
            i += 1
            continue
        out.append(char_map.get(ch, ch))
        i += 1
    return "".join(out)


def analyze_and_correct_persian(text: str) -> Dict[str, Any]:
    """Offline Persian spelling, homophone, ZWNJ (نیم‌فاصله), and punctuation corrector."""
    issues: List[Dict[str, str]] = []
    s = text

    # 1. Normalize Arabic Kaf / Yeh / Alef Maksura -> standard Persian ک / ی
    norm_kaf_yeh = s.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی").replace("\u06C0", "هٔ")
    if norm_kaf_yeh != s:
        issues.append({
            "original": "ك / ي عربی",
            "replacement": "ک / ی فارسی",
            "category": "نگارش فارسی",
            "explanation": "تبدیل حروف عربی (ك، ي) به حروف استاندارد فارسی (ک، ی)",
        })
        s = norm_kaf_yeh

    # 2. Persian ZWNJ (نیم‌فاصله) rules BEFORE word lookup so spaced verbs/plurals normalize cleanly
    # 2a. می / نمی + space + verb -> می‌ / نمی‌ + verb
    def _fix_mi_verb(m: re.Match) -> str:
        pref, verb = m.group(1), m.group(2)
        if verb in PERSIAN_NON_NOUN_BEFORE_HA or len(verb) < 2:
            return m.group(0)
        fixed = f"{pref}{ZWNJ}{verb}"
        issues.append({
            "original": m.group(0),
            "replacement": fixed,
            "category": "نیم‌فاصله",
            "explanation": f"رعایت نیم‌فاصله پس از «{pref}» در فعل «{fixed}»",
        })
        return fixed

    s = re.sub(r"(?<![آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی\u200C])(ن?می)[ \t]+([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی]{2,})", _fix_mi_verb, s)

    # 2b. Noun + space + ها / های / هایی / هایم / هایت / هایش -> ZWNJ
    def _fix_plural_ha(m: re.Match) -> str:
        noun, suf = m.group(1), m.group(2)
        if noun in PERSIAN_NON_NOUN_BEFORE_HA or noun in ("نمی", "می"):
            return m.group(0)
        fixed = f"{noun}{ZWNJ}{suf}"
        issues.append({
            "original": m.group(0),
            "replacement": fixed,
            "category": "نیم‌فاصله",
            "explanation": f"رعایت نیم‌فاصله پیش از نشانهٔ جمع «{suf}»",
        })
        return fixed

    s = re.sub(r"([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی]{2,})[ \t]+(ها(?:یی|یم|یت|یش|یمان|یتان|یشان|ی)?)(?![آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])", _fix_plural_ha, s)

    # 2c. Adjective + space + تر / ترین -> ZWNJ (or joined بهتر / بیشتر)
    def _fix_tar(m: re.Match) -> str:
        adj, suf = m.group(1), m.group(2)
        if adj in PERSIAN_NON_NOUN_BEFORE_HA:
            return m.group(0)
        if adj in ("به", "بیش"):
            fixed = f"{adj}{suf}"
        else:
            fixed = f"{adj}{ZWNJ}{suf}"
        issues.append({
            "original": m.group(0),
            "replacement": fixed,
            "category": "نیم‌فاصله",
            "explanation": f"رعایت نیم‌فاصله در صفت برتر/برترین «{fixed}»",
        })
        return fixed

    s = re.sub(r"([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی]{2,})[ \t]+(تر(?:ین)?)(?![آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])", _fix_tar, s)

    # 2d. Past participle (...ده / ...ته / ...ره / ...ده) + space + ام / ای / ایم / اید / اند -> ZWNJ
    def _fix_participle(m: re.Match) -> str:
        part, aux = m.group(1), m.group(2)
        if part in PERSIAN_NON_NOUN_BEFORE_HA or part in ("نه", "چه", "که", "به"):
            return m.group(0)
        fixed = f"{part}{ZWNJ}{aux}"
        issues.append({
            "original": m.group(0),
            "replacement": fixed,
            "category": "نیم‌فاصله",
            "explanation": f"رعایت نیم‌فاصله در ماضی نقلی «{fixed}»",
        })
        return fixed

    s = re.sub(r"([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی]{2,}[دترنملگکپبزسشخفوقعه]ه)[ \t]+(ام|ای|ایم|اید|اند)(?![آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])", _fix_participle, s)

    # 3. Word-level Persian misspellings, homophones, tanwin & fused می verbs
    fa_word_re = re.compile(r"[آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهیئؤإأٱ\u200C]+")

    def _fix_fa_word(m: re.Match) -> str:
        w = m.group(0)
        if w in PERSIAN_MISSPELLINGS:
            fixed = PERSIAN_MISSPELLINGS[w]
            if fixed != w:
                cat = "نیم‌فاصله" if ZWNJ in fixed and ZWNJ not in w else "املای فارسی"
                issues.append({
                    "original": w,
                    "replacement": fixed,
                    "category": cat,
                    "explanation": f"اصلاح «{w}» به «{fixed}»",
                })
                return fixed
        # Collapse 3+ repeated Persian characters (e.g. سلاااام -> سلام, چطوووره -> چطوره)
        collapsed = re.sub(r"([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])\1{2,}", r"\1", w)
        if collapsed != w:
            collapsed = PERSIAN_MISSPELLINGS.get(collapsed, collapsed)
            issues.append({
                "original": w,
                "replacement": collapsed,
                "category": "املای فارسی",
                "explanation": f"حذف تکرار حروف در «{w}» ← «{collapsed}»",
            })
            return collapsed
        # Windows fa-IR OS spellchecker high-confidence slip check (skip ZWNJ compounds)
        if ZWNJ not in w:
            cpp_sug = CPP_BRIDGE.suggest(w)
            if cpp_sug:
                cpp_sug = cpp_sug.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی")
            if cpp_sug and cpp_sug != w and " " not in cpp_sug and PERSIAN_SCRIPT_RE.search(cpp_sug):
                issues.append({
                    "original": w,
                    "replacement": cpp_sug,
                    "category": "املای فارسی",
                    "explanation": f"پیشنهاد املایی: «{cpp_sug}»",
                })
                return cpp_sug
        return w

    s = fa_word_re.sub(_fix_fa_word, s)

    # 4. Accidental duplicate Persian function words (من من، از از، به به excluded)
    def _dedup_fa(m: re.Match) -> str:
        w = m.group(1)
        if w in ("به", "کم", "تند", "آرام", "جدا", "دانه", "قطره", "تکه"):
            return m.group(0)
        issues.append({
            "original": m.group(0),
            "replacement": w,
            "category": "دستور فارسی",
            "explanation": f"حذف تکرار ناخواستهٔ واژهٔ «{w}»",
        })
        return w

    s = re.sub(r"(?<![آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی]{2,})[ \t]+\1(?![آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])", _dedup_fa, s)

    # 5. Persian punctuation (?, ; , -> ؟ ؛ ،) & spacing + missing question mark ؟
    punct_before = s
    s = s.replace("?", "؟").replace(";", "؛").replace(",", "،")
    s = re.sub(r"[ \t]+([،؛؟.!])", r"\1", s)
    s = re.sub(r"([،؛؟])([آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی])", r"\1 \2", s)
    s = re.sub(r"[ \t]{2,}", " ", s)
    if s != punct_before:
        issues.append({
            "original": "علائم نگارشی",
            "replacement": "نگارش استاندارد فارسی",
            "category": "نگارش فارسی",
            "explanation": "اصلاح علائم نگارشی فارسی (، ؛ ؟) و فاصله‌گذاری",
        })

    stripped = s.rstrip()
    if stripped and _is_persian_question_ending(stripped):
        if stripped.endswith("."):
            s = stripped[:-1] + "؟"
            issues.append({
                "original": ".",
                "replacement": "؟",
                "category": "نگارش فارسی",
                "explanation": "جملهٔ پرسشی فارسی باید با علامت سؤال «؟» پایان یابد",
            })
        elif stripped[-1] not in "؟.!،؛:":
            s = stripped + "؟"
            issues.append({
                "original": stripped.split()[-1],
                "replacement": stripped.split()[-1] + "؟",
                "category": "نگارش فارسی",
                "explanation": "افزودن علامت سؤال «؟» در پایان جملهٔ پرسشی",
            })

    diff = build_word_diff(text, s)
    count = len(issues)
    if count == 0:
        summary = "متن فارسی صحیح است ✓"
    else:
        f0 = issues[0]
        prev = f"{f0['original']} ← {f0['replacement']}"
        if len(prev) > 26:
            prev = f0["category"]
        summary = f"{count} اصلاح · {prev}"

    return {
        "ok": True,
        "lang": "fa",
        "original": text,
        "corrected": s,
        "issue_count": count,
        "issues": issues[:12],
        "diff": diff,
        "quick_summary": summary,
        "engine": "Python+C++ (فارسی)",
    }


def convert_finglish_to_persian(text: str) -> Dict[str, Any]:
    """Convert Latin-script Persian (Finglish / Pinglish) into standard Persian script."""
    working = text.strip()

    # 1. Apply multi-word Finglish phrases first
    for pat, fa_rep in FINGLISH_PHRASES:
        working = pat.sub(fa_rep, working)

    # 2. Convert remaining Latin tokens to Persian script
    def _tok_to_fa(m: re.Match) -> str:
        tok = m.group(0)
        low = tok.lower()
        if low in STRONG_FINGLISH_LEXICON:
            return STRONG_FINGLISH_LEXICON[low]
        if low in WEAK_FINGLISH_LEXICON:
            return WEAK_FINGLISH_LEXICON[low]
        return _phonetic_finglish_to_fa(low)

    converted = re.sub(r"[A-Za-z][A-Za-z']*", _tok_to_fa, working)

    # Natural greeting comma after initial سلام when followed by حال / چطور / خوب / چه
    converted = re.sub(r"^\s*سلام\s+(?=(?:حالت|حالتون|حال\s|چطور|چطوری|چطوره|خوبی|چه\s+خبر))", "سلام، ", converted)

    # Polish ZWNJ, Persian punctuation (?, , -> ؟ ،) & question marks via Persian analyzer
    polished = analyze_and_correct_persian(converted)["corrected"].strip()
    if polished and polished[-1] not in "؟.!":
        if _is_persian_question_ending(polished):
            polished += "؟"
        elif len(polished.split()) >= 4:
            polished += "."

    issues = [{
        "original": text.strip()[:36],
        "replacement": polished[:36],
        "category": "Finglish → فارسی",
        "explanation": "تبدیل هوشمند فینگلیش به خط استاندارد فارسی",
    }]
    diff = build_word_diff(text, polished)
    short_prev = polished if len(polished) <= 24 else (polished[:22] + "…")
    return {
        "ok": True,
        "lang": "finglish",
        "original": text,
        "corrected": polished,
        "issue_count": 1,
        "issues": issues,
        "diff": diff,
        "quick_summary": f"فارسی: {short_prev}",
        "engine": "Python+C++ (فینگلیش)",
    }


def reword_persian_offline(fa_base: str, mode: str, variant: int) -> Dict[str, Any]:
    """Offline Persian tone rewording (Fluent, Formal, Casual, Concise, Expand, Persuasive)."""
    s = fa_base.strip()
    variants: List[str] = []

    if mode == "formal":
        f1 = s
        formal_subs = [
            (r"حالت چطوره", "حال شما چطور است"),
            (r"حالتون چطوره", "حال شما چطور است"),
            (r"چطوری", "حال شما چطور است"),
            (r"چطوره", "چطور است"),
            (r"خوبم", "خوب هستم"),
            (r"خوبی", "خوب هستید"),
            (r"مرسی", "سپاسگزارم"),
            (r"ممنونم", "بسیار سپاسگزارم"),
            (r"می‌رم", "می‌روم"),
            (r"می‌خوام", "می‌خواهم"),
            (r"می‌تونم", "می‌توانم"),
            (r"نمی‌دونم", "نمی‌دانم"),
            (r"می‌دونم", "می‌دانم"),
            (r"می‌کنم", "انجام می‌دهم"),
            (r"خونه", "منزل"),
            (r"اگه", "چنانچه"),
            (r"واسه", "برای"),
            (r"\bتو\b", "شما"),
            (r"^سلام،?\s*", "با سلام و احترام، "),
        ]
        for pat, rep in formal_subs:
            f1 = re.sub(pat, rep, f1)
        f2 = f1.replace("با سلام و احترام، ", "عرض سلام و ادب؛ ") if "با سلام و احترام، " in f1 else ("با احترام، " + f1)
        variants = [f1, f2] if f2 != f1 else [f1]

    elif mode == "casual":
        c1 = s
        casual_subs = [
            (r"با سلام(?: و احترام)?،?\s*", "سلام! "),
            (r"حال شما چطور است", "حالت چطوره"),
            (r"چطور است", "چطوره"),
            (r"می‌روم", "می‌رم"),
            (r"می‌خواهم", "می‌خوام"),
            (r"می‌توانم", "می‌تونم"),
            (r"نمی‌دانم", "نمی‌دونم"),
            (r"می‌دانم", "می‌دونم"),
            (r"سپاسگزارم", "دمت گرم، مرسی"),
            (r"منزل", "خونه"),
            (r"خانه", "خونه"),
        ]
        for pat, rep in casual_subs:
            c1 = re.sub(pat, rep, c1)
        c2 = c1.replace("سلام، ", "سلام! ") if "سلام، " in c1 else c1
        variants = [c1, c2] if c2 != c1 else [c1]

    elif mode == "concise":
        k1 = re.sub(r"\b(?:راستش|در واقع|به نظر من|خیلی زیاد)\s*", "", s).strip()
        k1 = re.sub(r"سلام،?\s+حالت چطوره؟", "سلام، چطوری؟", k1)
        variants = [k1 or s]

    elif mode == "expand":
        core = s.rstrip(".!؟")
        if s.endswith("؟"):
            e1 = f"{s} امیدوارم همه چیز عالی پیش برود."
            e2 = f"{s} خوشحال می‌شوم نظر یا وضعیتتان را بدانم."
        else:
            e1 = f"{core} و امیدوارم بهترین نتیجه حاصل شود."
            e2 = f"به طور دقیق‌تر، {core} تا همه چیز به بهترین شکل انجام شود."
        variants = [e1, e2]

    elif mode == "persuasive":
        p1 = re.sub(r"\b(?:شاید|فکر کنم|احتمالاً)\s*", "", s).strip()
        p2 = f"بدون تردید، {p1}" if not p1.endswith("؟") else p1
        variants = [p1, p2] if p2 != p1 else [p1]

    else:  # fluent
        v1 = s
        v2 = s.replace("حالت چطوره؟", "حالتون چطوره؟") if "حالت چطوره؟" in s else s
        variants = [v1, v2] if v2 != v1 else [v1]

    chosen = variants[variant % len(variants)]
    return {
        "ok": True,
        "mode": mode,
        "result": chosen,
        "variants": variants,
        "engine": "Python+C++ (فارسی)",
    }


def analyze_and_correct(text: str) -> Dict[str, Any]:
    if not text or not text.strip():
        return {
            "ok": True,
            "original": text,
            "corrected": text,
            "issue_count": 0,
            "issues": [],
            "diff": [],
            "quick_summary": "Ready to write",
            "engine": "Python+C++" if CPP_BRIDGE.available else "Python",
        }

    lang_mode = detect_language_mode(text)
    if lang_mode == "finglish":
        return convert_finglish_to_persian(text)
    if lang_mode == "fa":
        return analyze_and_correct_persian(text)

    issues: List[Dict[str, str]] = []
    working = text

    # Step 1: Word-level spelling & proper capitalization
    working = apply_word_spelling_and_caps(working, issues)

    # Step 2: Phrase-level grammar rules
    for pattern, repl_fn, category, explanation in GRAMMAR_PHRASE_RULES:
        def _make_sub(pat_repl: Any, cat: str, expl: str):
            def _sub(m: re.Match) -> str:
                orig_span = m.group(0)
                if callable(pat_repl):
                    new_span = pat_repl(m)
                    if new_span is None:
                        return orig_span
                else:
                    new_span = match_case(orig_span, pat_repl)
                if new_span != orig_span:
                    issues.append({
                        "original": orig_span,
                        "replacement": new_span,
                        "category": cat,
                        "explanation": expl,
                    })
                    return new_span
                return orig_span
            return _sub

        working = pattern.sub(_make_sub(repl_fn, category, explanation), working)

    # Step 3: Phonetic article rules (a vs an)
    working = apply_article_rules(working, issues)

    # Step 4: Punctuation & sentence capitalization
    working = apply_punctuation_and_sentence_caps(working, issues)

    diff = build_word_diff(text, working)
    count = len(issues)
    if count == 0 and working != text:
        count = 1
        issues.append({
            "original": text.strip(),
            "replacement": working.strip(),
            "category": "Clarity",
            "explanation": "Polished text structure",
        })

    if count == 0:
        summary = "Looks clean ✓"
    else:
        first = issues[0]
        preview = f"{first['original']} → {first['replacement']}"
        if len(preview) > 26:
            preview = first["category"]
        noun = "fix" if count == 1 else "fixes"
        summary = f"{count} {noun} · {preview}"

    return {
        "ok": True,
        "original": text,
        "corrected": working,
        "issue_count": count,
        "issues": issues[:12],
        "diff": diff,
        "quick_summary": summary,
        "engine": "Python+C++" if CPP_BRIDGE.available else "Python",
    }


# ---------------------------------------------------------------------------
# Ultra-Fast Keep-Alive Cloud AI (Google Gemini + DeepSeek + Deepgram Voice/Aura)
# + Optional Local LLM Connector (Phi Silica / LM Studio / Ollama)
# ---------------------------------------------------------------------------
DEFAULT_GEMINI_MODEL = "gemini-3.1-flash-lite-preview"
DEFAULT_DEEPSEEK_MODEL = "deepseek-chat"
DEFAULT_DEEPGRAM_MODEL = "nova-2"

GEMINI_FALLBACK_MODELS = [
    "gemini-3.1-flash-lite-preview",
    "gemini-flash-lite-latest",
    "gemini-3-flash-preview",
    "gemini-3.5-flash-lite",
]
RETIRED_GEMINI_MODELS = {
    "gemini-2.5-flash",
    "gemini-2.5-flash-lite",
    "gemini-2.5-pro",
    "gemini-2.0-flash",
    "gemini-1.5-flash",
    "gemini-1.5-pro",
}

ENGINE_CONFIG: Dict[str, Any] = {
    "google_api_key": (os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY") or "").strip(),
    "google_model": DEFAULT_GEMINI_MODEL,
    "last_working_model": DEFAULT_GEMINI_MODEL,
    "deepseek_api_key": (os.environ.get("DEEPSEEK_API_KEY") or "").strip(),
    "deepseek_model": DEFAULT_DEEPSEEK_MODEL,
    "deepgram_api_key": (os.environ.get("DEEPGRAM_API_KEY") or "").strip(),
    "deepgram_language": "multi",  # "multi" | "en" | "fa"
    "ai_provider": "auto",  # "ai_only" | "auto" | "google" | "deepseek" | "local" | "offline"
}

_GEMINI_CACHE: Dict[Tuple[str, ...], Any] = {}
_RESOLVED_GEMINI_MODELS: Dict[str, str] = {}
_NO_THINKING_MODELS: set = set()
_LLM_CACHE: Dict[str, Any] = {"ts": 0.0, "info": None}
_PHI_CACHE: Dict[str, Any] = {"ts": 0.0, "code": -2, "label": ""}

# Persistent Keep-Alive HTTPS Connection Pool (eliminates ~500ms DNS + TCP + TLS handshake per request!)
_HTTP_CONNS: Dict[str, http.client.HTTPSConnection] = {}
_HTTP_LOCKS: Dict[str, threading.Lock] = {}
_HTTP_GLOBAL_LOCK = threading.Lock()


def _get_host_lock(host: str) -> threading.Lock:
    with _HTTP_GLOBAL_LOCK:
        if host not in _HTTP_LOCKS:
            _HTTP_LOCKS[host] = threading.Lock()
        return _HTTP_LOCKS[host]


def _https_request_keepalive(
    host: str,
    path: str,
    body: bytes,
    headers: Dict[str, str],
    timeout: float = 6.5,
    method: str = "POST",
) -> Tuple[int, bytes, Optional[str]]:
    lock = _get_host_lock(host)
    req_headers = dict(headers)
    req_headers["Connection"] = "keep-alive"
    req_headers["Content-Length"] = str(len(body))

    with lock:
        for attempt in range(2):
            conn = _HTTP_CONNS.get(host)
            try:
                if conn is None:
                    conn = http.client.HTTPSConnection(host, timeout=timeout)
                    _HTTP_CONNS[host] = conn
                else:
                    conn.timeout = timeout
                    if conn.sock is not None:
                        conn.sock.settimeout(timeout)

                conn.request(method, path, body=body, headers=req_headers)
                resp = conn.getresponse()
                raw_bytes = resp.read()
                status = int(resp.status)
                if resp.headers.get("Connection", "").lower() == "close":
                    try:
                        conn.close()
                    except Exception:
                        pass
                    _HTTP_CONNS.pop(host, None)
                return status, raw_bytes, None
            except Exception as exc:
                try:
                    if conn is not None:
                        conn.close()
                except Exception:
                    pass
                _HTTP_CONNS.pop(host, None)
                if attempt == 1:
                    return -1, b"", str(exc)
    return -1, b"", "Keep-alive request failed"


def _warmup_cloud_connections() -> None:
    """Warm up TLS sockets in background so first AI request has 0 ms TLS handshake latency."""
    def _worker() -> None:
        hosts = []
        if (ENGINE_CONFIG.get("google_api_key") or "").strip():
            hosts.append("generativelanguage.googleapis.com")
        if (ENGINE_CONFIG.get("deepseek_api_key") or "").strip():
            hosts.append("api.deepseek.com")
        if (ENGINE_CONFIG.get("deepgram_api_key") or "").strip():
            hosts.append("api.deepgram.com")
        for h in hosts:
            lock = _get_host_lock(h)
            with lock:
                if h not in _HTTP_CONNS:
                    try:
                        c = http.client.HTTPSConnection(h, timeout=3.5)
                        c.connect()
                        _HTTP_CONNS[h] = c
                    except Exception:
                        pass
    try:
        t = threading.Thread(target=_worker, daemon=True)
        t.start()
    except Exception:
        pass


def _extract_json_block(raw_text: str) -> Any:
    cleaned = raw_text.strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```(?:json)?\s*", "", cleaned, flags=re.IGNORECASE)
        cleaned = re.sub(r"\s*```$", "", cleaned)
    try:
        return json.loads(cleaned)
    except Exception:
        m = re.search(r"\{[\s\S]*\}", cleaned)
        if m:
            return json.loads(m.group(0))
        raise


def _call_single_gemini_model(
    prompt: str,
    key: str,
    mdl: str,
    expect_json: bool,
    timeout: float,
    max_tokens: int = 650,
) -> Tuple[Optional[str], Optional[str], int]:
    path = f"/v1beta/models/{mdl}:generateContent"
    use_zero_thinking = mdl not in _NO_THINKING_MODELS and ("flash" in mdl.lower() or "gemini-3" in mdl.lower() or "gemini-2.5" in mdl.lower())

    for retry_no_think in range(2):
        gen_cfg: Dict[str, Any] = {
            "temperature": 0.1 if expect_json else 0.25,
            "maxOutputTokens": max_tokens,
        }
        if expect_json:
            gen_cfg["responseMimeType"] = "application/json"
        if use_zero_thinking and retry_no_think == 0:
            # Disable hidden thinking tokens so Gemini responds in ~0.4-0.9s instead of 3-4s!
            gen_cfg["thinkingConfig"] = {"thinkingBudget": 0}

        body = json.dumps({
            "contents": [{"parts": [{"text": prompt}]}],
            "generationConfig": gen_cfg,
        }).encode("utf-8")

        status, resp_bytes, net_err = _https_request_keepalive(
            "generativelanguage.googleapis.com",
            path,
            body,
            {
                "Content-Type": "application/json",
                "x-goog-api-key": key,
            },
            timeout=timeout,
        )
        if net_err:
            return None, net_err, -1

        if status == 200:
            try:
                payload = json.loads(resp_bytes.decode("utf-8"))
                candidates = payload.get("candidates") or []
                if not candidates:
                    return None, "No candidates returned by Gemini API", 0
                parts = candidates[0].get("content", {}).get("parts") or []
                out_text = "".join(p.get("text", "") for p in parts if not p.get("thought")).strip()
                if not out_text:
                    out_text = "".join(p.get("text", "") for p in parts).strip()
                if not out_text:
                    return None, "Empty text response from Gemini API", 0
                return out_text, None, 200
            except Exception as exc:
                return None, str(exc), -1

        err_body = resp_bytes.decode("utf-8", errors="replace")
        try:
            err_json = json.loads(err_body)
            msg = err_json.get("error", {}).get("message") or err_body[:180]
        except Exception:
            msg = err_body[:180]

        # If a specific model does not accept thinkingConfig, retry immediately without it
        if status == 400 and use_zero_thinking and retry_no_think == 0 and ("thinking" in msg.lower() or "unknown name" in msg.lower()):
            _NO_THINKING_MODELS.add(mdl)
            continue

        return None, f"HTTP {status}: {msg}", status

    return None, "Gemini request failed", -1


def call_google_gemini(
    prompt: str,
    api_key: str = "",
    model: str = "",
    expect_json: bool = False,
    timeout: float = 6.0,
    max_tokens: int = 650,
) -> Tuple[Optional[str], Optional[str]]:
    key = (api_key or ENGINE_CONFIG.get("google_api_key") or "").strip()
    requested_mdl = (model or ENGINE_CONFIG.get("google_model") or DEFAULT_GEMINI_MODEL).strip()
    if not key:
        return None, "Google API key is empty"

    primary_mdl = _RESOLVED_GEMINI_MODELS.get(requested_mdl, requested_mdl)
    if primary_mdl in RETIRED_GEMINI_MODELS:
        primary_mdl = DEFAULT_GEMINI_MODEL

    try_order = [primary_mdl] + [m for m in GEMINI_FALLBACK_MODELS if m != primary_mdl]
    last_err: Optional[str] = None

    for idx, cand_mdl in enumerate(try_order):
        per_try_timeout = timeout if idx == 0 else min(5.0, timeout)
        out_text, err, status_code = _call_single_gemini_model(
            prompt, key, cand_mdl, expect_json, per_try_timeout, max_tokens=max_tokens
        )
        if out_text and not err:
            _RESOLVED_GEMINI_MODELS[requested_mdl] = cand_mdl
            ENGINE_CONFIG["last_working_model"] = cand_mdl
            return out_text, None

        last_err = err
        if status_code in (401,) or (status_code == 400 and err and "api key" in err.lower()):
            break

    return None, last_err


def call_deepseek_api(
    prompt: str,
    api_key: str = "",
    model: str = "",
    expect_json: bool = False,
    timeout: float = 7.0,
    max_tokens: int = 650,
) -> Tuple[Optional[str], Optional[str]]:
    key = (api_key or ENGINE_CONFIG.get("deepseek_api_key") or "").strip()
    mdl = (model or ENGINE_CONFIG.get("deepseek_model") or DEFAULT_DEEPSEEK_MODEL).strip()
    if not key:
        return None, "DeepSeek API key is empty"

    req_obj: Dict[str, Any] = {
        "model": mdl,
        "messages": [
            {
                "role": "system",
                "content": (
                    "You are FERDOWSI, an expert English, Persian (فارسی), and Finglish writing assistant. "
                    + ("Always respond in valid JSON format only." if expect_json else "Follow instructions strictly and output only the requested result.")
                ),
            },
            {"role": "user", "content": prompt},
        ],
        "temperature": 0.1 if expect_json else 0.25,
        "max_tokens": max_tokens,
        "stream": False,
    }
    if expect_json and mdl == "deepseek-chat":
        req_obj["response_format"] = {"type": "json_object"}

    body = json.dumps(req_obj).encode("utf-8")
    status, resp_bytes, net_err = _https_request_keepalive(
        "api.deepseek.com",
        "/chat/completions",
        body,
        {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {key}",
        },
        timeout=timeout,
    )
    if net_err:
        return None, net_err
    if status == 200:
        try:
            payload = json.loads(resp_bytes.decode("utf-8"))
            choices = payload.get("choices") or []
            if not choices:
                return None, "No choices returned by DeepSeek API"
            msg = choices[0].get("message") or {}
            out_text = str(msg.get("content") or "").strip()
            if not out_text:
                return None, "Empty response from DeepSeek API"
            return out_text, None
        except Exception as exc:
            return None, str(exc)

    err_body = resp_bytes.decode("utf-8", errors="replace")
    try:
        err_json = json.loads(err_body)
        err_msg = err_json.get("error", {}).get("message") or err_body[:180]
    except Exception:
        err_msg = err_body[:180]
    return None, f"HTTP {status}: {err_msg}"


def has_any_cloud_key() -> bool:
    g_key = (ENGINE_CONFIG.get("google_api_key") or "").strip()
    d_key = (ENGINE_CONFIG.get("deepseek_api_key") or "").strip()
    return bool(g_key or d_key)


def google_gemini_Enabled() -> bool:
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    if prov in ("offline", "local"):
        return False
    g_key = (ENGINE_CONFIG.get("google_api_key") or "").strip()
    d_key = (ENGINE_CONFIG.get("deepseek_api_key") or "").strip()
    if prov == "google":
        return bool(g_key)
    if prov == "deepseek":
        return bool(d_key)
    return bool(g_key or d_key)


def call_cloud_ai(
    prompt: str,
    expect_json: bool = False,
    timeout: float = 6.0,
    max_tokens: int = 650,
) -> Tuple[Optional[str], Optional[str], str]:
    """Unified Cloud AI dispatcher supporting Google Gemini and DeepSeek with automatic failover."""
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    g_key = (ENGINE_CONFIG.get("google_api_key") or "").strip()
    d_key = (ENGINE_CONFIG.get("deepseek_api_key") or "").strip()
    d_mdl = (ENGINE_CONFIG.get("deepseek_model") or DEFAULT_DEEPSEEK_MODEL).strip()

    # If user explicitly chose DeepSeek (or only has a DeepSeek key)
    if prov == "deepseek" or (prov in ("auto", "ai_only") and d_key and not g_key):
        out, err = call_deepseek_api(prompt, api_key=d_key, model=d_mdl, expect_json=expect_json, timeout=timeout, max_tokens=max_tokens)
        if out and not err:
            return out, None, f"DeepSeek ({d_mdl})"
        if g_key and prov in ("auto", "ai_only"):
            g_out, g_err = call_google_gemini(prompt, api_key=g_key, expect_json=expect_json, timeout=timeout, max_tokens=max_tokens)
            if g_out and not g_err:
                used_g = ENGINE_CONFIG.get("last_working_model", DEFAULT_GEMINI_MODEL)
                return g_out, None, f"Google Gemini ({used_g})"
        return None, err, f"DeepSeek ({d_mdl})"

    # Default Cloud Priority: Google Gemini -> failover to DeepSeek if configured
    if g_key:
        g_out, g_err = call_google_gemini(prompt, api_key=g_key, expect_json=expect_json, timeout=timeout, max_tokens=max_tokens)
        if g_out and not g_err:
            used_g = ENGINE_CONFIG.get("last_working_model", DEFAULT_GEMINI_MODEL)
            return g_out, None, f"Google Gemini ({used_g})"
        if d_key and prov in ("auto", "ai_only"):
            d_out, d_err = call_deepseek_api(prompt, api_key=d_key, model=d_mdl, expect_json=expect_json, timeout=timeout, max_tokens=max_tokens)
            if d_out and not d_err:
                return d_out, None, f"DeepSeek ({d_mdl})"
        return None, g_err, "Google Gemini"

    return None, "No Google Gemini or DeepSeek API key configured", "Cloud AI"


def transcribe_deepgram_audio(
    wav_path: str,
    api_key: str = "",
    language: str = "",
    model: str = DEFAULT_DEEPGRAM_MODEL,
) -> Dict[str, Any]:
    """Transcribe recorded 16kHz PCM WAV file via Deepgram Nova-2 / Aura Voice API."""
    key = (api_key or ENGINE_CONFIG.get("deepgram_api_key") or "").strip()
    lang = (language or ENGINE_CONFIG.get("deepgram_language") or "multi").strip().lower()
    if not key:
        return {"ok": False, "error": "Deepgram API key is not configured. Open ⚙ Settings in FERDOWSI to paste your Deepgram API key."}
    if not wav_path or not os.path.isfile(wav_path):
        return {"ok": False, "error": "Recorded audio file not found."}

    try:
        with open(wav_path, "rb") as fp:
            wav_bytes = fp.read()
    except Exception as exc:
        return {"ok": False, "error": f"Could not read audio: {exc}"}

    if len(wav_bytes) < 640:
        return {"ok": False, "error": "Recording was too short. Click 🎤, speak clearly, then click 🔴 to transcribe."}

    q_params = {
        "model": model or DEFAULT_DEEPGRAM_MODEL,
        "smart_format": "true",
        "punctuate": "true",
    }
    if lang in ("", "multi", "auto"):
        q_params["detect_language"] = "true"
    else:
        q_params["language"] = lang

    path = "/v1/listen?" + urllib.parse.urlencode(q_params)
    status, resp_bytes, net_err = _https_request_keepalive(
        "api.deepgram.com",
        path,
        wav_bytes,
        {
            "Authorization": f"Token {key}",
            "Content-Type": "audio/wav",
        },
        timeout=12.0,
    )
    if net_err:
        return {"ok": False, "error": f"Deepgram network error: {net_err}"}

    if status == 200:
        try:
            payload = json.loads(resp_bytes.decode("utf-8"))
            channels = payload.get("results", {}).get("channels") or []
            transcript = ""
            if channels:
                alts = channels[0].get("alternatives") or []
                if alts:
                    transcript = str(alts[0].get("transcript") or "").strip()
            return {
                "ok": True,
                "transcript": transcript,
                "engine": f"Deepgram Voice ({model or DEFAULT_DEEPGRAM_MODEL})",
            }
        except Exception as exc:
            return {"ok": False, "error": f"Invalid Deepgram response: {exc}"}

    err_body = resp_bytes.decode("utf-8", errors="replace")
    try:
        err_json = json.loads(err_body)
        err_msg = err_json.get("err_msg") or err_json.get("message") or err_body[:180]
    except Exception:
        err_msg = err_body[:180]
    return {"ok": False, "error": f"Deepgram HTTP {status}: {err_msg}"}


def test_deepgram_api(api_key: str = "") -> Tuple[bool, str]:
    """Verify a Deepgram API key by sending a tiny 0.15s silent WAV frame to /v1/listen."""
    key = (api_key or ENGINE_CONFIG.get("deepgram_api_key") or "").strip()
    if not key:
        return False, "Deepgram API key is empty"

    # Build a valid 0.15s 16kHz 16-bit mono silent WAV in memory (44-byte header + 4800 zero bytes)
    import struct
    pcm_len = 4800
    header = struct.pack(
        "<4sI4s4sIHHIIHH4sI",
        b"RIFF", 36 + pcm_len, b"WAVE", b"fmt ", 16, 1, 1, 16000, 32000, 2, 16, b"data", pcm_len
    )
    wav_bytes = header + (b"\x00" * pcm_len)
    status, resp_bytes, net_err = _https_request_keepalive(
        "api.deepgram.com",
        "/v1/listen?model=nova-2&smart_format=true",
        wav_bytes,
        {
            "Authorization": f"Token {key}",
            "Content-Type": "audio/wav",
        },
        timeout=7.0,
    )
    if net_err:
        return False, net_err
    if status == 200:
        return True, "Deepgram Voice / Aura API (nova-2) verified & ready for microphone dictation!"
    err_body = resp_bytes.decode("utf-8", errors="replace")
    try:
        err_json = json.loads(err_body)
        err_msg = err_json.get("err_msg") or err_json.get("message") or err_body[:160]
    except Exception:
        err_msg = err_body[:160]
    return False, f"HTTP {status}: {err_msg}"


def try_google_gemini_analyze(text: str, force_lang: str = "") -> Optional[Dict[str, Any]]:
    if not google_gemini_Enabled() or not text or not text.strip():
        return None
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    mdl_tag = ENGINE_CONFIG.get("deepseek_model", DEFAULT_DEEPSEEK_MODEL) if prov == "deepseek" else ENGINE_CONFIG.get("google_model", DEFAULT_GEMINI_MODEL)
    lang_mode = force_lang or detect_language_mode(text)
    cache_key = ("analyze", prov, mdl_tag, lang_mode, text)
    if cache_key in _GEMINI_CACHE:
        return dict(_GEMINI_CACHE[cache_key])

    if lang_mode == "finglish":
        prompt = (
            "Convert the ENTIRE Finglish (Latin-script Persian) input into natural, properly spelled Persian script (فارسی).\n"
            "Use standard Persian 'ک'/'ی', proper نیم‌فاصله ('\\u200c' in می‌..., ها, تر), and Persian punctuation ('،', '؟', '.').\n"
            "Return JSON ONLY:\n"
            '{"corrected": "متن کامل به خط فارسی", "issues": [{"original": "finglish", "replacement": "فارسی", "category": "Finglish → فارسی", "explanation": "تبدیل به خط استاندارد فارسی"}]}\n\n'
            f"Input:\n{text}"
        )
    elif lang_mode == "fa":
        prompt = (
            "Fix all Persian spelling, homophones (صلام->سلام, سپاسگذارم->سپاسگزارم), missing نیم‌فاصله ('\\u200c'), Arabic 'ك'/'ي', grammar, and punctuation across the full text.\n"
            "Return JSON ONLY (max 5 issues):\n"
            '{"corrected": "متن کامل ویرایش‌شده فارسی", "issues": [{"original": "نادرست", "replacement": "درست", "category": "املای فارسی|نیم‌فاصله|دستور فارسی", "explanation": "توضیح کوتاه"}]}\n\n'
            f"Input:\n{text}"
        )
    else:
        prompt = (
            "Inspect EVERY word in the input text and fix all spelling (including repeated letters or split words like 'chec k'->'check'), grammar, tense, agreement, article, preposition, and punctuation errors while preserving meaning.\n"
            "Return JSON ONLY (max 5 concise issues):\n"
            '{"corrected": "full corrected text", "issues": [{"original": "wrong", "replacement": "fixed", "category": "Grammar|Spelling|Punctuation", "explanation": "brief reason"}]}\n\n'
            f"Input:\n{text}"
        )

    raw, err, engine_label = call_cloud_ai(prompt, expect_json=True, timeout=6.0, max_tokens=550)
    if err or not raw:
        return None
    try:
        parsed = _extract_json_block(raw)
        corrected = str(parsed.get("corrected", text)).strip()
        if not corrected:
            return None
        if lang_mode in ("fa", "finglish") or PERSIAN_SCRIPT_RE.search(corrected):
            corrected = corrected.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی")

        default_cat = "Finglish → فارسی" if lang_mode == "finglish" else ("املای فارسی" if lang_mode == "fa" else "Grammar")
        raw_issues = parsed.get("issues") or []
        issues: List[Dict[str, str]] = []
        for it in raw_issues[:8]:
            if isinstance(it, dict):
                orig_s = str(it.get("original", "")).strip()
                repl_s = str(it.get("replacement", "")).strip()
                if orig_s != repl_s:
                    issues.append({
                        "original": orig_s or "text",
                        "replacement": repl_s or "fixed",
                        "category": str(it.get("category", default_cat)),
                        "explanation": str(it.get("explanation", f"Corrected by {engine_label}")),
                    })
        diff = build_word_diff(text, corrected)
        if not issues and corrected != text.strip():
            issues.append({
                "original": text.strip()[:28],
                "replacement": corrected[:28],
                "category": default_cat,
                "explanation": f"Polished via {engine_label}",
            })
        count = len(issues)
        if lang_mode == "finglish" and count > 0:
            short_prev = corrected if len(corrected) <= 24 else (corrected[:22] + "…")
            summary = f"فارسی: {short_prev}"
        elif lang_mode == "fa":
            if count == 0:
                summary = "متن فارسی صحیح است ✓"
            else:
                f0 = issues[0]
                prev = f"{f0['original']} ← {f0['replacement']}"
                if len(prev) > 26:
                    prev = f0["category"]
                summary = f"{count} اصلاح · {prev}"
        else:
            if count == 0:
                summary = "Looks clean ✓"
            else:
                f0 = issues[0]
                prev = f"{f0['original']} → {f0['replacement']}"
                if len(prev) > 26:
                    prev = f0["category"]
                summary = f"{count} {'fix' if count == 1 else 'fixes'} · {prev}"

        result = {
            "ok": True,
            "lang": lang_mode,
            "original": text,
            "corrected": corrected,
            "issue_count": count,
            "issues": issues,
            "diff": diff,
            "quick_summary": summary,
            "engine": engine_label,
        }
        if len(_GEMINI_CACHE) > 150:
            _GEMINI_CACHE.clear()
        _GEMINI_CACHE[cache_key] = dict(result)
        return result
    except Exception:
        return None


def try_google_gemini_reword(text: str, mode: str, variant: int) -> Optional[Dict[str, Any]]:
    if not google_gemini_Enabled() or not text or not text.strip():
        return None
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    mdl_tag = ENGINE_CONFIG.get("deepseek_model", DEFAULT_DEEPSEEK_MODEL) if prov == "deepseek" else ENGINE_CONFIG.get("google_model", DEFAULT_GEMINI_MODEL)
    lang_mode = detect_language_mode(text)
    cache_key = ("reword", prov, mdl_tag, lang_mode, mode, variant > 0, text)
    if cache_key in _GEMINI_CACHE:
        variants, cached_label = _GEMINI_CACHE[cache_key]
        return {
            "ok": True,
            "mode": mode,
            "result": variants[variant % len(variants)],
            "variants": variants,
            "engine": cached_label,
        }

    # Fast path for initial tone click (variant == 0): direct single-text output in <0.8s!
    if variant == 0:
        if lang_mode in ("fa", "finglish"):
            fa_tones = {
                "fluent": "روان، طبیعی و معیار به زبان فارسی",
                "formal": "رسمی، محترمانه و اداری به زبان فارسی",
                "casual": "صمیمی، دوستانه و محاوره‌ای طبیعی به زبان فارسی",
                "concise": "کوتاه، موجز و دقیق به زبان فارسی",
                "expand": "کامل، مشروح و خوش‌پرداخت به زبان فارسی",
                "persuasive": "تأثیرگذار و متقاعدکننده به زبان فارسی",
            }
            fa_desc = fa_tones.get(mode, fa_tones["fluent"])
            prompt = (
                f"Rewrite the input text in standard Persian script (فارسی) with tone: {fa_desc}. "
                "Fix all spelling/نیم‌فاصله errors and convert Finglish if needed. "
                "Output ONLY the final Persian text with no quotes or explanation.\n\n"
                f"Text:\n{text}"
            )
        else:
            en_tones = {
                "fluent": "smooth, natural, native-level fluent English",
                "formal": "polished, formal, professional English",
                "casual": "warm, friendly, conversational English",
                "concise": "concise, punchy, and direct English",
                "expand": "well-elaborated, complete English",
                "persuasive": "confident, persuasive, action-oriented English",
            }
            en_desc = en_tones.get(mode, en_tones["fluent"])
            prompt = (
                f"Rewrite the input text fixing all typos/grammar in this tone: {en_desc}. "
                "Output ONLY the rewritten text with no quotes or explanation.\n\n"
                f"Text:\n{text}"
            )
        raw, err, engine_label = call_cloud_ai(prompt, expect_json=False, timeout=5.5, max_tokens=360)
        if not err and raw:
            clean_out = raw.strip().strip("\"'")
            if lang_mode in ("fa", "finglish") or PERSIAN_SCRIPT_RE.search(clean_out):
                clean_out = clean_out.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی")
            if clean_out:
                variants = [clean_out]
                _GEMINI_CACHE[cache_key] = (variants, engine_label)
                return {
                    "ok": True,
                    "mode": mode,
                    "result": clean_out,
                    "variants": variants,
                    "engine": engine_label,
                }

    # Multi-variant path when cycling alternatives (variant > 0)
    if lang_mode in ("fa", "finglish"):
        prompt = (
            f"Rewrite the input text in standard Persian script (فارسی) in a '{mode}' tone. "
            'Return JSON ONLY: {"variants": ["بازنویسی اول", "بازنویسی دوم"]}\n\n'
            f"Input:\n{text}"
        )
    else:
        prompt = (
            f"Rewrite the input text fixing all typos in a '{mode}' tone. "
            'Return JSON ONLY: {"variants": ["first rewrite", "second alternative rewrite"]}\n\n'
            f"Input:\n{text}"
        )

    raw, err, engine_label = call_cloud_ai(prompt, expect_json=True, timeout=6.0, max_tokens=480)
    if err or not raw:
        return None
    try:
        parsed = _extract_json_block(raw)
        vars_raw = parsed.get("variants") or []
        variants = []
        for v in vars_raw:
            vs = str(v).strip()
            if vs:
                if lang_mode in ("fa", "finglish") or PERSIAN_SCRIPT_RE.search(vs):
                    vs = vs.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی")
                variants.append(vs)
        if not variants:
            return None
        _GEMINI_CACHE[cache_key] = (variants, engine_label)
        return {
            "ok": True,
            "mode": mode,
            "result": variants[variant % len(variants)],
            "variants": variants,
            "engine": engine_label,
        }
    except Exception:
        return None


def try_cloud_ai_format(text: str, mode: str) -> Optional[Dict[str, Any]]:
    """Direct single-pass Cloud AI formatting (<0.8s instead of 4s!)."""
    if not google_gemini_Enabled() or not text or not text.strip():
        return None
    fmt_mode = (mode or "sentence_case").lower().strip()
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    mdl_tag = ENGINE_CONFIG.get("deepseek_model", DEFAULT_DEEPSEEK_MODEL) if prov == "deepseek" else ENGINE_CONFIG.get("google_model", DEFAULT_GEMINI_MODEL)
    cache_key = ("format_fast", prov, mdl_tag, fmt_mode, text)
    if cache_key in _GEMINI_CACHE:
        return dict(_GEMINI_CACHE[cache_key])

    format_prompts = {
        "finglish_to_fa": (
            "Convert the entire input from Finglish (or Persian with errors) into natural, properly spelled Persian script (فارسی) "
            "with standard 'ک'/'ی' and proper نیم‌فاصله ('\\u200c'). Output ONLY the Persian text."
        ),
        "persian_zwnj": (
            "Fix all Persian spelling, grammar, and Zero-Width Non-Joiners (نیم‌فاصله '\\u200c' in می‌..., ها, تر, ام/ای/است) "
            "across the input text. Output ONLY the corrected Persian text."
        ),
        "sentence_case": (
            "Fix any spelling/grammar typos and format the text in clean Sentence case. Output ONLY the formatted text."
        ),
        "title_case": (
            "Fix any spelling typos and format the text in standard Title Case. Output ONLY the Title Case text."
        ),
        "bullet_list": (
            "Fix any spelling/grammar errors and format the key points or clauses of the text into a clean bulleted list "
            "(each line starting with '• '). Output ONLY the bulleted list."
        ),
        "numbered_list": (
            "Fix any spelling/grammar errors and format the items or clauses of the text into a clean numbered list "
            "(1. ..., 2. ...). Output ONLY the numbered list."
        ),
        "email_format": (
            "Fix all spelling/grammar errors and format the text as a polite, well-structured email with an appropriate greeting "
            "and sign-off (in Persian if the text is Persian/Finglish, otherwise in English). Output ONLY the formatted email."
        ),
        "clean_text": (
            "Clean up extra spaces, broken punctuation, and fix all spelling and grammar mistakes in the text. "
            "Output ONLY the cleaned text."
        ),
    }

    # Instant deterministic transforms still carry the active AI badge when AI is enabled
    if fmt_mode in ("upper_case", "lower_case", "snake_case", "camel_case"):
        det = format_text(text, fmt_mode)["result"]
        _, _, eng_label = call_cloud_ai("ok", expect_json=False, timeout=2.0, max_tokens=5) if not _GEMINI_CACHE else ("ok", None, f"Google Gemini ({mdl_tag})" if prov != "deepseek" else f"DeepSeek ({mdl_tag})")
        if not eng_label or eng_label == "Cloud AI":
            eng_label = f"DeepSeek ({mdl_tag})" if prov == "deepseek" else f"Google Gemini ({mdl_tag})"
        return {"ok": True, "mode": fmt_mode, "result": det, "engine": eng_label}

    instruction = format_prompts.get(fmt_mode, format_prompts["clean_text"])
    prompt = f"{instruction}\nDo not include quotes or markdown code blocks.\n\nInput:\n{text}"
    raw, err, engine_label = call_cloud_ai(prompt, expect_json=False, timeout=5.5, max_tokens=420)
    if err or not raw:
        return None

    res_str = raw.strip()
    if res_str.startswith("```"):
        res_str = re.sub(r"^```[a-zA-Z]*\s*", "", res_str).rstrip("`").strip()
    if fmt_mode not in ("bullet_list", "numbered_list", "email_format"):
        res_str = res_str.strip("\"'")
    if PERSIAN_SCRIPT_RE.search(res_str):
        res_str = res_str.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی")

    out_obj = {
        "ok": True,
        "mode": fmt_mode,
        "result": res_str,
        "engine": engine_label,
    }
    _GEMINI_CACHE[cache_key] = dict(out_obj)
    return out_obj


def _norm_persian_chars(s: str) -> str:
    return s.replace("\u0643", "ک").replace("\u064A", "ی").replace("\u0649", "ی")


OFFLINE_BILINGUAL_DICT: Dict[str, Dict[str, Any]] = {
    "resilient": {
        "pos": "adjective",
        "phonetic": "/rɪˈzɪliənt/",
        "definition": "Able to withstand or recover quickly from difficult conditions.",
        "fa": "تاب‌آور، مقاوم، انعطاف‌پذیر",
        "meanings": ["تاب‌آور", "مقاوم", "انعطاف‌پذیر", "سرسخت"],
        "syn": ["tough (مقاوم)", "adaptable (سازگار)", "strong (قوی)", "hardy (سرسخت)"],
        "ant": ["fragile (شکننده)", "vulnerable (آسیب‌پذیر)", "weak (ضعیف)"],
        "examples": [
            {"en": "She remained resilient despite all the unexpected setbacks.", "fa": "او با وجود همه موانع غیرمنتظره، تاب‌آور و مقاوم باقی ماند."},
            {"en": "Engineers built a resilient system that recovers automatically.", "fa": "مهندسان سیستمی مقاوم ساختند که به‌صورت خودکار بازیابی می‌شود."},
        ],
    },
    "elaborate": {
        "pos": "adjective / verb",
        "phonetic": "/ɪˈlæbərət/",
        "definition": "Involving many carefully arranged parts; or to explain in greater detail.",
        "fa": "مفصل، دقیق، باجزئیات؛ توضیح بیشتر دادن",
        "meanings": ["مفصل", "دقیق", "پرجزئیات", "توضیح دادن"],
        "syn": ["detailed (باجزئیات)", "intricate (پیچیده و ظریف)", "thorough (کامل)", "expand (بسط دادن)"],
        "ant": ["simple (ساده)", "brief (مختصر)", "plain (ساده و بی‌پیرایه)"],
        "examples": [
            {"en": "Could you elaborate on your proposal during the meeting?", "fa": "آیا می‌توانید در جلسه درباره پیشنهادتان بیشتر توضیح دهید؟"},
            {"en": "They prepared an elaborate plan for the product launch.", "fa": "آن‌ها برنامه‌ای مفصل و دقیق برای عرضه محصول آماده کردند."},
        ],
    },
    "cursor": {
        "pos": "noun",
        "phonetic": "/ˈkɜːrsər/",
        "definition": "A movable indicator on a computer screen identifying the active position.",
        "fa": "نشانگر، مکان‌نما",
        "meanings": ["نشانگر", "مکان‌نما", "نشانگر ماوس/متن"],
        "syn": ["pointer (اشاره‌گر)", "caret (مکان‌نمای متن)", "indicator (نشانگر)"],
        "ant": ["background (پس‌زمینه)", "fixed point (نقطه ثابت)"],
        "examples": [
            {"en": "The assistant appears right above your cursor while you type.", "fa": "دستیار هنگام تایپ دقیقاً بالای مکان‌نمای شما ظاهر می‌شود."},
            {"en": "Move the cursor to the end of the line and press Enter.", "fa": "مکان‌نما را به انتهای خط ببرید و کلید اینتر را بزنید."},
        ],
    },
    "accurate": {
        "pos": "adjective",
        "phonetic": "/ˈækjərət/",
        "definition": "Correct in all details; exact and free from error.",
        "fa": "دقیق، درست، بی‌نقص",
        "meanings": ["دقیق", "صحیح", "درست"],
        "syn": ["precise (دقیق)", "exact (عیناً درست)", "correct (صحیح)"],
        "ant": ["inaccurate (نادقیق)", "wrong (نادرست)", "flawed (خطادار)"],
        "examples": [
            {"en": "The translation was remarkably accurate and natural.", "fa": "ترجمه به‌طرز چشمگیری دقیق و طبیعی بود."},
            {"en": "Please provide an accurate estimate of the project timeline.", "fa": "لطفاً برآورد دقیقی از زمان‌بندی پروژه ارائه دهید."},
        ],
    },
}


def try_google_gemini_dictionary(text: str) -> Optional[Dict[str, Any]]:
    if not has_any_cloud_key() or not text or not text.strip():
        return None
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    mdl_tag = ENGINE_CONFIG.get("deepseek_model", DEFAULT_DEEPSEEK_MODEL) if prov == "deepseek" else ENGINE_CONFIG.get("google_model", DEFAULT_GEMINI_MODEL)
    clean_q = text.strip()
    lang_mode = detect_language_mode(clean_q)
    cache_key = ("dictionary", prov, mdl_tag, lang_mode, clean_q)
    if cache_key in _GEMINI_CACHE:
        return dict(_GEMINI_CACHE[cache_key])

    prompt = (
        "You are an expert bilingual English <-> Persian (فارسی) AI dictionary and translator.\n"
        "Use standard Persian 'ک'/'ی' and proper نیم‌فاصله ('\\u200c').\n"
        "Return valid JSON with this EXACT structure:\n"
        "{\n"
        '  "headword": "normalized word or phrase",\n'
        '  "is_sentence": false,\n'
        '  "part_of_speech": "noun | verb | adjective | adverb | phrase | sentence",\n'
        '  "phonetic": "/IPA/",\n'
        '  "definition": "concise English definition",\n'
        '  "persian_translation": "accurate fluent Persian (فارسی) translation",\n'
        '  "english_translation": "accurate English translation",\n'
        '  "persian_meanings": ["معنی ۱", "معنی ۲", "معنی ۳"],\n'
        '  "synonyms": ["syn1 (معادل)", "syn2 (معادل)", "syn3 (معادل)"],\n'
        '  "antonyms": ["ant1 (متضاد)", "ant2 (متضاد)"],\n'
        '  "examples": [\n'
        '    {"en": "First English example sentence.", "fa": "ترجمه فارسی جمله نمونه اول."},\n'
        '    {"en": "Second English example sentence.", "fa": "ترجمه فارسی جمله نمونه دوم."}\n'
        "  ]\n"
        "}\n\n"
        f"Input:\n{clean_q}"
    )

    raw, err, engine_label = call_cloud_ai(prompt, expect_json=True, timeout=8.5, max_tokens=950)
    if err or not raw:
        return None
    try:
        parsed = _extract_json_block(raw)
        if not isinstance(parsed, dict):
            return None
        fa_trans = _norm_persian_chars(str(parsed.get("persian_translation", "")).strip())
        en_trans = str(parsed.get("english_translation", "")).strip()
        if not fa_trans and not en_trans:
            return None

        meanings_raw = parsed.get("persian_meanings") or []
        meanings = [_norm_persian_chars(str(m).strip()) for m in meanings_raw if str(m).strip()][:6]

        syn_raw = parsed.get("synonyms") or []
        synonyms = [_norm_persian_chars(str(s).strip()) for s in syn_raw if str(s).strip()][:6]

        ant_raw = parsed.get("antonyms") or []
        antonyms = [_norm_persian_chars(str(a).strip()) for a in ant_raw if str(a).strip()][:6]

        ex_raw = parsed.get("examples") or []
        examples: List[Dict[str, str]] = []
        for ex in ex_raw[:3]:
            if isinstance(ex, dict):
                en_s = str(ex.get("en", "")).strip()
                fa_s = _norm_persian_chars(str(ex.get("fa", "")).strip())
                if en_s or fa_s:
                    examples.append({"en": en_s, "fa": fa_s})

        word_count = len(clean_q.split())
        is_sent = bool(parsed.get("is_sentence", word_count > 4))
        result = {
            "ok": True,
            "query": clean_q,
            "is_sentence": is_sent,
            "lang": lang_mode,
            "headword": _norm_persian_chars(str(parsed.get("headword") or clean_q[:48]).strip()),
            "part_of_speech": str(parsed.get("part_of_speech") or ("sentence" if is_sent else "word")).strip(),
            "phonetic": str(parsed.get("phonetic") or "").strip(),
            "definition": str(parsed.get("definition") or "").strip(),
            "persian_translation": fa_trans or (meanings[0] if meanings else ""),
            "english_translation": en_trans or clean_q,
            "persian_meanings": meanings,
            "synonyms": synonyms,
            "antonyms": antonyms,
            "examples": examples,
            "engine": engine_label,
        }
        if len(_GEMINI_CACHE) > 150:
            _GEMINI_CACHE.clear()
        _GEMINI_CACHE[cache_key] = dict(result)
        return result
    except Exception:
        return None


def offline_dictionary_lookup(text: str) -> Dict[str, Any]:
    """Offline bilingual fallback when cloud/local AI is unreachable."""
    clean_q = (text or "").strip()
    if not clean_q:
        return {"ok": True, "query": "", "persian_translation": "", "english_translation": "", "persian_meanings": [], "synonyms": [], "antonyms": [], "examples": [], "engine": "Offline Dictionary"}

    lang_mode = detect_language_mode(clean_q)
    low = clean_q.lower().strip(".,!?؟،؛\"'")

    if low in OFFLINE_BILINGUAL_DICT:
        entry = OFFLINE_BILINGUAL_DICT[low]
        return {
            "ok": True,
            "query": clean_q,
            "is_sentence": False,
            "lang": "en",
            "headword": low,
            "part_of_speech": entry["pos"],
            "phonetic": entry["phonetic"],
            "definition": entry["definition"],
            "persian_translation": entry["fa"],
            "english_translation": low,
            "persian_meanings": list(entry["meanings"]),
            "synonyms": list(entry["syn"]),
            "antonyms": list(entry["ant"]),
            "examples": list(entry["examples"]),
            "engine": "Built-in Bilingual Dictionary (Offline)",
        }

    if lang_mode == "finglish":
        fa_conv = convert_finglish_to_persian(clean_q)["corrected"]
        return {
            "ok": True,
            "query": clean_q,
            "is_sentence": len(clean_q.split()) > 3,
            "lang": "finglish",
            "headword": fa_conv,
            "part_of_speech": "Finglish → فارسی",
            "phonetic": "",
            "definition": "Transliterated from Finglish into standard Persian script (connect Google Gemini API for full AI definitions & examples).",
            "persian_translation": fa_conv,
            "english_translation": clean_q,
            "persian_meanings": [fa_conv],
            "synonyms": [reword_persian_offline(fa_conv, "formal", 0)["result"], reword_persian_offline(fa_conv, "casual", 0)["result"]],
            "antonyms": ["— (نیازمند اتصال به هوش مصنوعی Google Gemini)"],
            "examples": [{"en": clean_q, "fa": fa_conv}],
            "engine": "Offline Finglish Dictionary",
        }

    if lang_mode == "fa":
        fa_clean = analyze_and_correct_persian(clean_q)["corrected"]
        # Reverse lookup in Finglish lexicon for a basic offline Roman/English hint
        rev_matches = [k for k, v in STRONG_FINGLISH_LEXICON.items() if v == fa_clean.strip("؟.!،")]
        hint = rev_matches[0] if rev_matches else "Configure Google Gemini API in ⚙ Settings for full AI English translation"
        return {
            "ok": True,
            "query": clean_q,
            "is_sentence": len(clean_q.split()) > 3,
            "lang": "fa",
            "headword": fa_clean,
            "part_of_speech": "عبارت فارسی" if len(clean_q.split()) > 1 else "واژه فارسی",
            "phonetic": "",
            "definition": "واژه یا جمله فارسی (برای ترجمه کامل هوش مصنوعی و مترادف/متضاد، کلید Google Gemini را فعال نگه دارید).",
            "persian_translation": fa_clean,
            "english_translation": hint,
            "persian_meanings": [fa_clean],
            "synonyms": [reword_persian_offline(fa_clean, "formal", 0)["result"]],
            "antonyms": ["—"],
            "examples": [{"en": "", "fa": fa_clean}],
            "engine": "Offline Persian Dictionary",
        }

    # Generic English word/sentence offline fallback
    corr = analyze_and_correct(clean_q)["corrected"]
    is_sent = len(clean_q.split()) > 3
    return {
        "ok": True,
        "query": clean_q,
        "is_sentence": is_sent,
        "lang": "en",
        "headword": corr,
        "part_of_speech": "sentence" if is_sent else "word / phrase",
        "phonetic": "",
        "definition": "Add or enable your Google Gemini API key in ⚙ Settings for full AI Persian translation, synonyms, antonyms, and bilingual examples.",
        "persian_translation": "برای ترجمه هوشمند فارسی، کلید Google Gemini را در تنظیمات (⚙) فعال کنید.",
        "english_translation": corr,
        "persian_meanings": [],
        "synonyms": [reword_text(corr, "formal", 0, False)["result"]] if is_sent else [],
        "antonyms": [],
        "examples": [{"en": corr, "fa": ""}],
        "engine": "Offline Fallback (AI Key Required for Full Translation)",
    }


def dictionary_lookup(text: str, use_llm: bool = True) -> Dict[str, Any]:
    """AI-powered English <-> Persian dictionary & sentence translator (Persian meanings, Syn/Ant, Examples)."""
    if not text or not text.strip():
        return offline_dictionary_lookup("")

    prov = ENGINE_CONFIG.get("ai_provider", "auto")

    # 1. Always prefer Cloud AI (Google Gemini / DeepSeek) when a key is present
    if (use_llm or prov in ("ai_only", "google", "deepseek")) and has_any_cloud_key():
        gem_dict = try_google_gemini_dictionary(text)
        if gem_dict:
            return gem_dict

    # 2. Secondary fallback: Local LLM (LM Studio / Ollama) if running locally
    if use_llm or prov in ("ai_only", "local"):
        info = detect_local_llm()
        if info["available"] and info["provider"] in ("LM Studio", "Ollama"):
            local_res = try_local_llm_rewrite(
                text,
                "Translate this English/Persian word or sentence into Persian (فارسی), list 3 synonyms and 2 antonyms, and give 1 example sentence.",
                0,
            )
            if local_res:
                out_str, label = local_res
                return {
                    "ok": True,
                    "query": text.strip(),
                    "is_sentence": len(text.split()) > 4,
                    "lang": detect_language_mode(text),
                    "headword": text.strip()[:48],
                    "part_of_speech": "AI Dictionary",
                    "phonetic": "",
                    "definition": "Generated by local LLM",
                    "persian_translation": out_str,
                    "english_translation": text.strip(),
                    "persian_meanings": [],
                    "synonyms": [],
                    "antonyms": [],
                    "examples": [],
                    "engine": label,
                }

    # 3. Built-in Offline Bilingual Dictionary fallback
    return offline_dictionary_lookup(text)


def _port_open(host: str, port: int, timeout: float = 0.04) -> bool:
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False


def _cached_phi_status() -> Tuple[int, str]:
    now = time.monotonic()
    if _PHI_CACHE["code"] != -2 and (now - _PHI_CACHE["ts"]) < 20.0:
        return _PHI_CACHE["code"], _PHI_CACHE["label"]
    code, label = CPP_BRIDGE.phi_status()
    _PHI_CACHE["ts"] = now
    _PHI_CACHE["code"] = code
    _PHI_CACHE["label"] = label
    return code, label


def detect_local_llm() -> Dict[str, Any]:
    now = time.monotonic()
    if _LLM_CACHE["info"] is not None and (now - _LLM_CACHE["ts"]) < 15.0:
        return _LLM_CACHE["info"]
    phi_code, phi_label = _cached_phi_status()
    if phi_code == 0:
        res = {"available": True, "provider": "Phi Silica NPU", "url": "winrt://Microsoft.Windows.AI.Text.LanguageModel", "phi_state": phi_code, "phi_label": phi_label}
    elif _port_open("127.0.0.1", 1234):
        res = {"available": True, "provider": "LM Studio", "url": "http://127.0.0.1:1234/v1/chat/completions", "phi_state": phi_code, "phi_label": phi_label}
    elif _port_open("127.0.0.1", 11434):
        res = {"available": True, "provider": "Ollama", "url": "http://127.0.0.1:11434/api/generate", "phi_state": phi_code, "phi_label": phi_label}
    else:
        res = {"available": False, "provider": "None", "url": "", "phi_state": phi_code, "phi_label": phi_label}
    _LLM_CACHE["ts"] = now
    _LLM_CACHE["info"] = res
    return res


def try_local_llm_rewrite(text: str, instruction: str, tone_mode: int = 1) -> Optional[Tuple[str, str]]:
    # Priority 1: Windows Copilot+ NPU Phi Silica
    phi_code, _ = _cached_phi_status()
    if phi_code == 0:
        phi_res = CPP_BRIDGE.phi_rewrite(text, tone_mode, instruction)
        if phi_res:
            rewritten, phi_ms = phi_res
            return rewritten, f"Phi Silica NPU ({phi_ms} ms)"

    info = detect_local_llm()
    if not info["available"]:
        return None
    prompt = (
        f"{instruction}\n"
        f"Return ONLY the rewritten text with no quotes, no markdown, and no explanation.\n\n"
        f"Text: {text}"
    )
    try:
        if info["provider"] == "LM Studio":
            payload = json.dumps({
                "messages": [
                    {"role": "system", "content": "You are FERDOWSI, an expert writing assistant. Output only the rewritten text."},
                    {"role": "user", "content": prompt},
                ],
                "temperature": 0.4,
                "max_tokens": 300,
            }).encode("utf-8")
            req = urllib.request.Request(info["url"], data=payload, headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=4.5) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                out = data["choices"][0]["message"]["content"].strip()
                if out:
                    return out.strip("\"'"), "Local-LLM (LM Studio)"
        elif info["provider"] == "Ollama":
            # Query first available model
            tags_req = urllib.request.Request("http://127.0.0.1:11434/api/tags")
            with urllib.request.urlopen(tags_req, timeout=1.5) as tr:
                models = json.loads(tr.read().decode("utf-8")).get("models", [])
            if not models:
                return None
            model_name = models[0]["name"]
            payload = json.dumps({
                "model": model_name,
                "prompt": prompt,
                "stream": False,
            }).encode("utf-8")
            req = urllib.request.Request(info["url"], data=payload, headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=5.0) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                out = data.get("response", "").strip()
                if out:
                    return out.strip("\"'"), f"Local-LLM (Ollama:{model_name})"
    except Exception:
        return None
    return None


# ---------------------------------------------------------------------------
# Rewording / Paraphrasing Engine (6 Modes + Multiple Variants)
# ---------------------------------------------------------------------------
CONTRACTIONS_EXPAND = [
    (r"\bdon't\b", "do not"), (r"\bdoesn't\b", "does not"), (r"\bdidn't\b", "did not"),
    (r"\bcan't\b", "cannot"), (r"\bwon't\b", "will not"), (r"\bwouldn't\b", "would not"),
    (r"\bcouldn't\b", "could not"), (r"\bshouldn't\b", "should not"),
    (r"\bisn't\b", "is not"), (r"\baren't\b", "are not"), (r"\bwasn't\b", "was not"),
    (r"\bweren't\b", "were not"), (r"\bhaven't\b", "have not"), (r"\bhasn't\b", "has not"),
    (r"\bhadn't\b", "had not"), (r"\bI'm\b", "I am"), (r"\bI've\b", "I have"),
    (r"\bI'll\b", "I will"), (r"\bI'd\b", "I would"), (r"\byou're\b", "you are"),
    (r"\bwe're\b", "we are"), (r"\bthey're\b", "they are"), (r"\bit's\b", "it is"),
    (r"\bthat's\b", "that is"), (r"\bthere's\b", "there is"), (r"\blet's\b", "let us"),
]

CONTRACTIONS_SHRINK = [
    (r"\bdo not\b", "don't"), (r"\bdoes not\b", "doesn't"), (r"\bdid not\b", "didn't"),
    (r"\bcannot\b", "can't"), (r"\bcan not\b", "can't"), (r"\bwill not\b", "won't"),
    (r"\bwould not\b", "wouldn't"), (r"\bcould not\b", "couldn't"), (r"\bshould not\b", "shouldn't"),
    (r"\bis not\b", "isn't"), (r"\bare not\b", "aren't"), (r"\bI am\b", "I'm"),
    (r"\bI have\b", "I've"), (r"\bI will\b", "I'll"), (r"\byou are\b", "you're"),
    (r"\bwe are\b", "we're"), (r"\bthey are\b", "they're"), (r"\bit is\b", "it's"),
    (r"\bthat is\b", "that's"),
]

WORDY_TO_CONCISE = [
    (r"\bin order to\b", "to"),
    (r"\bdue to the fact that\b", "because"),
    (r"\bbecause of the fact that\b", "because"),
    (r"\bat this point in time\b", "now"),
    (r"\bat the present time\b", "now"),
    (r"\bin the event that\b", "if"),
    (r"\bfor the purpose of\b", "to"),
    (r"\bhas the ability to\b", "can"),
    (r"\bhave the ability to\b", "can"),
    (r"\bis able to\b", "can"),
    (r"\bare able to\b", "can"),
    (r"\bmake a decision\b", "decide"),
    (r"\btake into consideration\b", "consider"),
    (r"\bgive an explanation\b", "explain"),
    (r"\ba large number of\b", "many"),
    (r"\ba lot of stuff\b", "the details"),
    (r"\ba lot of\b", "many"),
    (r"\bright now\b", "now"),
    (r"\bvery good\b", "excellent"),
    (r"\bvery bad\b", "poor"),
    (r"\bvery important\b", "essential"),
    (r"\bvery big\b", "large"),
    (r"\bvery small\b", "tiny"),
    (r"\bvery fast\b", "rapid"),
    (r"\b(?:basically|actually|literally|honestly|really|just|kind of|sort of)\s+", ""),
    (r"\bI think that\s+", ""),
    (r"\bI think\s+", ""),
    (r"\bit seems that\s+", ""),
]

INFORMAL_TO_FORMAL = [
    (r"^(?:hey|hi|yo)\b[,\s]*", ""),
    (r"\bcan you\b", "could you please"),
    (r"\bneed to\b", "must"),
    (r"\bget\b", "obtain"),
    (r"\bgot\b", "received"),
    (r"\bhelp\b", "assist"),
    (r"\bshow\b", "demonstrate"),
    (r"\bcheck\b", "verify"),
    (r"\bfix\b", "resolve"),
    (r"\bproblem\b", "issue"),
    (r"\bask for\b", "request"),
    (r"\btalk about\b", "discuss"),
    (r"\bfind out\b", "determine"),
    (r"\bright now\b", "at your earliest convenience"),
    (r"\basap\b", "at your earliest convenience"),
    (r"\ba lot of\b", "several"),
    (r"\bstuff\b", "items"),
    (r"\bthings\b", "matters"),
    (r"\bkids\b", "children"),
    (r"\bbuy\b", "purchase"),
    (r"\bstart\b", "initiate"),
    (r"\buse\b", "utilize"),
    (r"\bthanks\b", "thank you"),
    (r"\bsorry\b", "I apologize"),
    (r"\bokay\b", "acceptable"),
    (r"\bok\b", "acceptable"),
    (r"\bawesome\b", "excellent"),
    (r"\bgreat\b", "remarkable"),
]

FORMAL_TO_CASUAL = [
    (r"\bcould you please\b", "could you"),
    (r"\bat your earliest convenience\b", "when you get a chance"),
    (r"\bassist\b", "help"),
    (r"\bobtain\b", "get"),
    (r"\bacquire\b", "get"),
    (r"\bpurchase\b", "buy"),
    (r"\butilize\b", "use"),
    (r"\bverify\b", "check"),
    (r"\bresolve\b", "fix"),
    (r"\bdemonstrate\b", "show"),
    (r"\bdetermine\b", "figure out"),
    (r"\bregarding\b", "about"),
    (r"\bconcerning\b", "about"),
    (r"\badditionally\b", "also"),
    (r"\bfurthermore\b", "plus"),
    (r"\btherefore\b", "so"),
    (r"\bsubsequently\b", "later"),
    (r"\binitiate\b", "start"),
    (r"\bcommence\b", "start"),
    (r"\bterminate\b", "end"),
    (r"\bI apologize\b", "sorry"),
    (r"\bnumerous\b", "lots of"),
]


def _sub_rules(text: str, rules: List[Tuple[str, str]]) -> str:
    s = text
    for pat, rep in rules:
        def _r(m: re.Match, target: str = rep) -> str:
            if not target:
                return ""
            return match_case(m.group(0), target)
        s = re.sub(pat, _r, s, flags=re.IGNORECASE)
    s = re.sub(r"[ \t]{2,}", " ", s).strip()
    if s and s[0].islower():
        s = s[0].upper() + s[1:]
    return s


def _ensure_terminal_punct(s: str, default: str = ".") -> str:
    s = s.strip()
    if not s:
        return s
    if s[-1] not in ".!?":
        first = re.sub(r"[^a-z]", "", s.split()[0].lower())
        if first in {"can", "could", "would", "will", "should", "do", "does", "did", "is", "are", "may"}:
            return s + "?"
        return s + default
    return s


def reword_text(text: str, mode: str = "fluent", variant: int = 0, use_llm: bool = False) -> Dict[str, Any]:
    if not text or not text.strip():
        return {"ok": True, "mode": mode, "result": text, "variants": [text], "engine": "Python+C++"}

    mode = (mode or "fluent").lower().strip()
    prov = ENGINE_CONFIG.get("ai_provider", "auto")
    lang_mode = detect_language_mode(text)

    if (use_llm or prov in ("ai_only", "google", "deepseek")) and prov != "offline":
        # Priority 1: Cloud AI (Google Gemini / DeepSeek — supports English, Persian & Finglish->Persian)
        if prov in ("ai_only", "auto", "google", "deepseek") and google_gemini_Enabled():
            gem_res = try_google_gemini_reword(text, mode, variant)
            if gem_res:
                return gem_res

        # Priority 2: Local AI (Phi Silica NPU -> LM Studio -> Ollama) for English
        if prov in ("ai_only", "auto", "local") and lang_mode == "en":
            prompts = {
                "fluent": "Rewrite this text so it sounds smooth, natural, and fluent while keeping the exact meaning.",
                "formal": "Rewrite this text in a polished, formal, professional tone suitable for business or academic writing.",
                "casual": "Rewrite this text in a warm, friendly, conversational, casual tone.",
                "concise": "Rewrite this text to be as concise, direct, and clear as possible without losing key facts.",
                "expand": "Expand and elaborate on this text into a fuller, well-articulated, complete statement.",
                "persuasive": "Rewrite this text in a confident, persuasive, action-oriented tone.",
            }
            tone_map = {"fluent": 1, "casual": 2, "concise": 3, "formal": 4, "expand": 0, "persuasive": 0}
            llm_res = try_local_llm_rewrite(text, prompts.get(mode, prompts["fluent"]), tone_map.get(mode, 1))
            if llm_res:
                rewritten, engine_label = llm_res
                return {
                    "ok": True,
                    "mode": mode,
                    "result": rewritten,
                    "variants": [rewritten],
                    "engine": engine_label,
                }

        if prov == "ai_only":
            return {
                "ok": True,
                "mode": mode,
                "result": text,
                "variants": [text],
                "engine": "AI Only (Add Google Gemini or DeepSeek API Key in ⚙ Settings)",
            }

    base = analyze_and_correct(text)["corrected"].strip()
    if lang_mode in ("fa", "finglish") or PERSIAN_SCRIPT_RE.search(base):
        return reword_persian_offline(base, mode, variant)

    variants: List[str] = []

    if mode == "formal":
        v1 = _sub_rules(base, CONTRACTIONS_EXPAND)
        v1 = _sub_rules(v1, WORDY_TO_CONCISE[:6])
        v1 = _sub_rules(v1, INFORMAL_TO_FORMAL)
        v1 = _ensure_terminal_punct(v1)

        v2 = v1
        if not v1.lower().startswith(("please", "could you", "kindly", "we ", "i ")):
            v2 = "Please note: " + v1[0].lower() + v1[1:]
        elif v1.lower().startswith("could you please "):
            body = v1[len("could you please "):].rstrip("?.")
            v2 = f"I would appreciate it if you could {body}."
        variants = [v1, v2] if v2 != v1 else [v1]

    elif mode == "casual":
        v1 = _sub_rules(base, FORMAL_TO_CASUAL)
        v1 = _sub_rules(v1, CONTRACTIONS_SHRINK)
        v1 = _ensure_terminal_punct(v1)

        v2 = v1
        if not re.match(r"^(hey|hi|just)\b", v1, re.IGNORECASE):
            v2 = "Hey — " + v1[0].lower() + v1[1:]
        variants = [v1, v2] if v2 != v1 else [v1]

    elif mode == "concise":
        v1 = _sub_rules(base, WORDY_TO_CONCISE)
        v1 = re.sub(r"^(?:Hey|Hi|Hello)[,\s]+", "", v1)
        v1 = re.sub(r"\bcan you\s+", "please ", v1, flags=re.IGNORECASE)
        v1 = re.sub(r"\bcould you(?: please)?\s+", "please ", v1, flags=re.IGNORECASE)
        if v1 and v1[0].islower():
            v1 = v1[0].upper() + v1[1:]
        if v1.lower().startswith("please ") and v1.endswith("?"):
            v1 = v1[:-1] + "."
        v1 = _ensure_terminal_punct(v1)
        variants = [v1]

    elif mode == "expand":
        core = base.rstrip(".!?")
        is_q = base.endswith("?")
        if is_q:
            v1 = f"Could you please let me know {core[0].lower() + core[1:]} when you have a moment?"
            v2 = f"{base} Any additional context or details you can share would be greatly appreciated."
        else:
            v1 = f"{core}, ensuring everything works smoothly and reliably from start to finish."
            v2 = f"Specifically, {core[0].lower() + core[1:]} so that we can achieve the best possible outcome."
        variants = [v1, v2]

    elif mode == "persuasive":
        v1 = re.sub(r"\b(?:maybe|perhaps|possibly|hopefully|I guess|I think|sort of|kind of)\s*", "", base, flags=re.IGNORECASE)
        v1 = re.sub(r"\btry to\s+", "", v1, flags=re.IGNORECASE)
        v1 = _sub_rules(v1, [(r"\bgood\b", "compelling"), (r"\bhelp\b", "empower"), (r"\bfix\b", "decisively resolve"), (r"\bmake\b", "deliver")])
        v1 = _ensure_terminal_punct(v1)
        v2 = f"To achieve immediate results, {v1[0].lower() + v1[1:]}" if not v1.endswith("?") else v1
        variants = [v1, v2] if v2 != v1 else [v1]

    else:  # fluent (default)
        v1 = _sub_rules(base, [
            (r"\bin order to\b", "to"),
            (r"\bdue to the fact that\b", "because"),
            (r"\ba lot of stuff\b", "several details"),
            (r"\bright now\b", "right away"),
            (r"\bcan you\b", "could you"),
        ])
        v1 = _ensure_terminal_punct(v1)
        v2 = _sub_rules(v1, CONTRACTIONS_SHRINK)
        variants = [v1, v2] if v2 != v1 else [v1]

    chosen = variants[variant % len(variants)]
    return {
        "ok": True,
        "mode": mode,
        "result": chosen,
        "variants": variants,
        "engine": "Python+C++" if CPP_BRIDGE.available else "Python",
    }


# ---------------------------------------------------------------------------
# Formatting Engine (9 Word & Sentence Format Modes)
# ---------------------------------------------------------------------------
MINOR_TITLE_WORDS = {
    "a", "an", "the", "and", "but", "or", "nor", "for", "yet", "so",
    "at", "by", "in", "of", "on", "to", "up", "as", "via", "per", "with", "from"
}


def _split_list_items(text: str) -> List[str]:
    lines = [ln.strip(" \t•-*") for ln in text.splitlines() if ln.strip(" \t•-*")]
    if len(lines) > 1:
        items = [re.sub(r"^\d+[.)]\s*", "", ln) for ln in lines]
    else:
        single = text.strip()
        # Split on sentence boundaries, semicolons (; / ؛), or comma/and clauses (, / ، / و)
        if ";" in single or "؛" in single:
            items = [p.strip() for p in re.split(r"[;؛]", single) if p.strip()]
        elif len(re.findall(r"[.!?؟]+(?:\s+|$)", single)) >= 2:
            items = [p.strip() for p in re.split(r"(?<=[.!?؟])\s+", single) if p.strip()]
        elif "," in single or "،" in single:
            items = [re.sub(r"^(?:and|or|و)\s+", "", p.strip(), flags=re.IGNORECASE)
                     for p in re.split(r"[,،]", single) if p.strip()]
        else:
            items = [p.strip() for p in re.split(r"\s+(?:and|then|also|و|سپس)\s+", single, flags=re.IGNORECASE) if p.strip()]

    cleaned: List[str] = []
    for it in items:
        corr = analyze_and_correct(it)["corrected"].strip().rstrip(".")
        if corr and corr[0].islower():
            corr = corr[0].upper() + corr[1:]
        if corr:
            cleaned.append(corr)
    return cleaned or [text.strip()]


def format_text(text: str, mode: str = "sentence_case") -> Dict[str, Any]:
    if not text or not text.strip():
        return {"ok": True, "mode": mode, "result": text}

    mode = (mode or "sentence_case").lower().strip()
    raw = text.strip()

    if mode == "finglish_to_fa":
        if PERSIAN_SCRIPT_RE.search(raw):
            res = analyze_and_correct_persian(raw)["corrected"]
        else:
            res = convert_finglish_to_persian(raw)["corrected"]

    elif mode == "persian_zwnj":
        if detect_language_mode(raw) == "finglish":
            res = convert_finglish_to_persian(raw)["corrected"]
        else:
            res = analyze_and_correct_persian(raw)["corrected"]

    elif mode == "upper_case":
        res = raw.upper()

    elif mode == "lower_case":
        res = raw.lower()

    elif mode == "sentence_case":
        low = raw.lower()
        # Capitalize start of sentences and proper tokens
        res = re.sub(r"(^\s*|[.!?]\s+)([a-z])", lambda m: m.group(1) + m.group(2).upper(), low)
        for k, v in PROPER_CAPITALIZATION.items():
            res = re.sub(r"\b" + re.escape(k) + r"\b", v, res, flags=re.IGNORECASE)

    elif mode == "title_case":
        tokens = re.findall(r"\S+|\s+", raw)
        word_indices = [idx for idx, tok in enumerate(tokens) if not tok.isspace()]
        out_tokens = list(tokens)
        for pos, idx in enumerate(word_indices):
            tok = tokens[idx]
            bare = re.sub(r"^[^A-Za-z0-9+#]+|[^A-Za-z0-9+#]+$", "", tok)
            low = bare.lower()
            is_edge = (pos == 0 or pos == len(word_indices) - 1)
            if low in PROPER_CAPITALIZATION:
                cased = PROPER_CAPITALIZATION[low]
            elif bare.isupper() and len(bare) >= 2:
                cased = bare
            elif not is_edge and low in MINOR_TITLE_WORDS:
                cased = low
            else:
                cased = bare[:1].upper() + bare[1:].lower()
            out_tokens[idx] = tok.replace(bare, cased, 1) if bare else tok
        res = "".join(out_tokens)

    elif mode == "bullet_list":
        items = _split_list_items(raw)
        res = "\n".join(f"• {it}" for it in items)

    elif mode == "numbered_list":
        items = _split_list_items(raw)
        res = "\n".join(f"{i}. {it}" for i, it in enumerate(items, 1))

    elif mode == "email_format":
        polished = analyze_and_correct(raw)["corrected"].strip()
        if PERSIAN_SCRIPT_RE.search(polished):
            if polished and polished[-1] not in ".!؟":
                polished += "؟" if _is_persian_question_ending(polished) else "."
            res = f"با سلام و احترام،\n\n{polished}\n\nبا سپاس،"
        else:
            polished = _ensure_terminal_punct(polished)
            res = f"Hi,\n\n{polished}\n\nBest regards,"

    elif mode == "clean_text":
        s = raw.replace("“", "\"").replace("”", "\"").replace("’", "'").replace("‘", "'")
        s = re.sub(r"[ \t]+", " ", s)
        s = re.sub(r"\n{3,}", "\n\n", s)
        s = re.sub(r"([!?.,؟،])\1+", r"\1", s)
        res = analyze_and_correct(s)["corrected"]

    elif mode == "snake_case":
        words = re.findall(r"[A-Za-z0-9]+", raw.lower())
        res = "_".join(words)

    elif mode == "camel_case":
        words = re.findall(r"[A-Za-z0-9]+", raw.lower())
        res = words[0] + "".join(w.capitalize() for w in words[1:]) if words else raw

    else:
        res = raw

    return {"ok": True, "mode": mode, "result": res}


# ---------------------------------------------------------------------------
# JSON-RPC Stdin/Stdout Server & CLI Mode
# ---------------------------------------------------------------------------
def handle_request(req: Dict[str, Any]) -> Dict[str, Any]:
    t0 = time.perf_counter()
    req_id = req.get("id", 0)
    action = req.get("action", "analyze")
    text = req.get("text", "")

    if action == "configure":
        if "google_api_key" in req:
            ENGINE_CONFIG["google_api_key"] = str(req.get("google_api_key") or "").strip()
        if "google_model" in req and req.get("google_model"):
            ENGINE_CONFIG["google_model"] = str(req.get("google_model")).strip()
        if "deepseek_api_key" in req:
            ENGINE_CONFIG["deepseek_api_key"] = str(req.get("deepseek_api_key") or "").strip()
        if "deepseek_model" in req and req.get("deepseek_model"):
            ENGINE_CONFIG["deepseek_model"] = str(req.get("deepseek_model")).strip()
        if "deepgram_api_key" in req:
            ENGINE_CONFIG["deepgram_api_key"] = str(req.get("deepgram_api_key") or "").strip()
        if "deepgram_language" in req and req.get("deepgram_language"):
            ENGINE_CONFIG["deepgram_language"] = str(req.get("deepgram_language")).strip().lower()
        if "ai_provider" in req and req.get("ai_provider"):
            ENGINE_CONFIG["ai_provider"] = str(req.get("ai_provider")).strip().lower()
        _GEMINI_CACHE.clear()
        _warmup_cloud_connections()
        return {
            "id": req_id,
            "ok": True,
            "google_configured": bool(ENGINE_CONFIG["google_api_key"]),
            "google_model": ENGINE_CONFIG["google_model"],
            "deepseek_configured": bool(ENGINE_CONFIG["deepseek_api_key"]),
            "deepseek_model": ENGINE_CONFIG["deepseek_model"],
            "deepgram_configured": bool(ENGINE_CONFIG["deepgram_api_key"]),
            "ai_provider": ENGINE_CONFIG["ai_provider"],
            "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
        }

    if action == "test_google":
        test_key = str(req.get("google_api_key") or ENGINE_CONFIG.get("google_api_key") or "").strip()
        test_model = str(req.get("google_model") or ENGINE_CONFIG.get("google_model") or DEFAULT_GEMINI_MODEL).strip()
        sample_out, err = call_google_gemini(
            "Correct the grammar of this sentence and output ONLY the corrected sentence: Ok, what dddo you meaan if I chec k this for you.",
            api_key=test_key,
            model=test_model,
            expect_json=False,
            timeout=8.0,
            max_tokens=120,
        )
        if err:
            return {
                "id": req_id,
                "ok": False,
                "error": err,
                "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
            }
        return {
            "id": req_id,
            "ok": True,
            "model": ENGINE_CONFIG.get("last_working_model", test_model),
            "sample_output": sample_out,
            "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
        }

    if action == "test_deepseek":
        test_key = str(req.get("deepseek_api_key") or ENGINE_CONFIG.get("deepseek_api_key") or "").strip()
        test_model = str(req.get("deepseek_model") or ENGINE_CONFIG.get("deepseek_model") or DEFAULT_DEEPSEEK_MODEL).strip()
        sample_out, err = call_deepseek_api(
            "Correct the grammar of this sentence and output ONLY the corrected sentence: Ok, what dddo you meaan if I chec k this for you.",
            api_key=test_key,
            model=test_model,
            expect_json=False,
            timeout=9.0,
            max_tokens=120,
        )
        if err:
            return {
                "id": req_id,
                "ok": False,
                "error": err,
                "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
            }
        return {
            "id": req_id,
            "ok": True,
            "model": test_model,
            "sample_output": sample_out,
            "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
        }

    if action == "test_deepgram":
        test_key = str(req.get("deepgram_api_key") or ENGINE_CONFIG.get("deepgram_api_key") or "").strip()
        ok_dg, msg_dg = test_deepgram_api(api_key=test_key)
        return {
            "id": req_id,
            "ok": ok_dg,
            "model": DEFAULT_DEEPGRAM_MODEL,
            "sample_output": msg_dg if ok_dg else "",
            "error": "" if ok_dg else msg_dg,
            "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
        }

    if action == "transcribe_audio":
        wav_path = str(req.get("wav_path") or "").strip()
        dg_res = transcribe_deepgram_audio(
            wav_path=wav_path,
            api_key=str(req.get("deepgram_api_key") or ENGINE_CONFIG.get("deepgram_api_key") or "").strip(),
            language=str(req.get("deepgram_language") or ENGINE_CONFIG.get("deepgram_language") or "multi").strip(),
        )
        dg_res["id"] = req_id
        dg_res["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
        return dg_res

    if action == "ping":
        llm = detect_local_llm()
        phi_code, phi_label = CPP_BRIDGE.phi_status()
        _warmup_cloud_connections()
        return {
            "id": req_id,
            "ok": True,
            "cpp_dll_loaded": CPP_BRIDGE.available,
            "python_version": sys.version.split()[0],
            "local_llm": llm,
            "phi_silica_state": phi_code,
            "phi_silica_label": phi_label,
            "google_configured": bool(ENGINE_CONFIG.get("google_api_key")),
            "google_model": ENGINE_CONFIG.get("google_model", DEFAULT_GEMINI_MODEL),
            "deepseek_configured": bool(ENGINE_CONFIG.get("deepseek_api_key")),
            "deepseek_model": ENGINE_CONFIG.get("deepseek_model", DEFAULT_DEEPSEEK_MODEL),
            "deepgram_configured": bool(ENGINE_CONFIG.get("deepgram_api_key")),
            "ai_provider": ENGINE_CONFIG.get("ai_provider", "auto"),
            "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
        }

    if action == "analyze" or action == "correct":
        use_llm = bool(req.get("use_llm", False))
        fast_badge_only = bool(req.get("fast_badge_only", False))
        prov = ENGINE_CONFIG.get("ai_provider", "auto")

        # When AI-Only / Google / DeepSeek is active, always run Cloud AI (even if fast_badge_only was requested on ai_only)
        force_cloud = prov in ("ai_only", "google", "deepseek")
        if (use_llm or force_cloud) and (not fast_badge_only or prov == "ai_only") and prov in ("ai_only", "auto", "google", "deepseek") and google_gemini_Enabled() and text and text.strip():
            gem_resp = try_google_gemini_analyze(text)
            if gem_resp:
                gem_resp["id"] = req_id
                gem_resp["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
                return gem_resp

        # Local AI (Phi Silica NPU / LM Studio / Ollama)
        if (use_llm or prov in ("ai_only", "local")) and not fast_badge_only and prov in ("ai_only", "auto", "local") and text and text.strip() and detect_language_mode(text) == "en":
            local_fix = try_local_llm_rewrite(
                text,
                "Fix all spelling, grammar, verb tense, word choice, and punctuation errors across the entire text while keeping the original meaning and tone.",
                0,
            )
            if local_fix:
                llm_corr, llm_label = local_fix
                diff = build_word_diff(text, llm_corr)
                issues = []
                if llm_corr and llm_corr != text.strip():
                    issues.append({
                        "original": text.strip()[:30],
                        "replacement": llm_corr.strip()[:30],
                        "category": "AI Grammar",
                        "explanation": f"Full-sentence grammar correction via {llm_label}",
                    })
                return {
                    "id": req_id,
                    "ok": True,
                    "lang": "en",
                    "original": text,
                    "corrected": llm_corr or text,
                    "issue_count": len(issues),
                    "issues": issues,
                    "diff": diff,
                    "quick_summary": f"1 fix · {llm_label.split(' ')[0]}" if issues else "Looks clean ✓",
                    "engine": llm_label,
                    "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
                }

        # Strict AI-Only Guard: never fall back to Python+C++ when user selected "ai_only"
        if prov == "ai_only":
            return {
                "id": req_id,
                "ok": True,
                "lang": detect_language_mode(text),
                "original": text,
                "corrected": text,
                "issue_count": 0,
                "issues": [],
                "diff": [{"op": "eq", "text": text}],
                "quick_summary": "Add AI Key in ⚙ Settings",
                "engine": "AI Only (Configure Google Gemini or DeepSeek API Key in ⚙ Settings)",
                "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
            }

        resp = analyze_and_correct(text)
        resp["id"] = req_id
        resp["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
        return resp

    if action == "reword":
        resp = reword_text(
            text=text,
            mode=req.get("mode", "fluent"),
            variant=int(req.get("variant", 0)),
            use_llm=bool(req.get("use_llm", False)),
        )
        resp["id"] = req_id
        resp["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
        return resp

    if action == "format":
        fmt_mode = str(req.get("mode", "sentence_case")).lower().strip()
        use_llm = bool(req.get("use_llm", False))
        prov = ENGINE_CONFIG.get("ai_provider", "auto")

        # Fast single-pass Cloud AI formatting (<0.8s instead of 4s, and returns Cloud AI engine badge!)
        if (use_llm or prov in ("ai_only", "google", "deepseek")) and google_gemini_Enabled() and text and text.strip():
            ai_fmt = try_cloud_ai_format(text, fmt_mode)
            if ai_fmt:
                ai_fmt["id"] = req_id
                ai_fmt["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
                return ai_fmt

        if prov == "ai_only":
            return {
                "id": req_id,
                "ok": True,
                "mode": fmt_mode,
                "result": text,
                "engine": "AI Only (Add Google Gemini or DeepSeek API Key in ⚙ Settings)",
                "elapsed_ms": round((time.perf_counter() - t0) * 1000.0, 2),
            }

        resp = format_text(text=text, mode=fmt_mode)
        resp["engine"] = "Python+C++" if CPP_BRIDGE.available else "Python"
        resp["id"] = req_id
        resp["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
        return resp

    if action == "dictionary":
        resp = dictionary_lookup(text=text, use_llm=bool(req.get("use_llm", True)))
        resp["id"] = req_id
        resp["elapsed_ms"] = round((time.perf_counter() - t0) * 1000.0, 2)
        return resp
    return {"id": req_id, "ok": False, "error": f"Unknown action: {action}"}


def serve_stdio() -> None:
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8", errors="replace")
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            if req.get("action") == "shutdown":
                sys.stdout.write(json.dumps({"id": req.get("id", 0), "ok": True}) + "\n")
                sys.stdout.flush()
                break
            resp = handle_request(req)
        except Exception as exc:
            resp = {"id": 0, "ok": False, "error": str(exc)}
        sys.stdout.write(json.dumps(resp, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--cli":
        sample = sys.argv[2] if len(sys.argv) > 2 else "i has went to teh store yesterday for buying a apple"
        print(json.dumps(analyze_and_correct(sample), indent=2, ensure_ascii=False))
    else:
        serve_stdio()
