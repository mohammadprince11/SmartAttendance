import json
import sys

def line(index, text, box=None):
    return {
        "index": index,
        "text": text,
        "score": 0.99,
        "box": box,
    }

def national_id_lines():
    return [
        line(0, "البطاقة الوطنية اكارني نيشتمانى", [421, 685, 761, 747]),
        line(1, "199276728473", [429, 724, 767, 787]),
        line(2, "محمد", [796, 816, 924, 861]),
        line(3, "الاسم لاناو", [947, 806, 1120, 865]),
        line(4, "علي", [812, 861, 934, 922]),
        line(5, "الاب باوك", [910, 856, 1122, 919]),
        line(6, "الجدبابير زيدان", [794, 911, 1122, 972]),
        line(7, "اللقبنارناو السوداني", [742, 962, 1121, 1028]),
        line(8, "الأم دايك سهاد", [805, 1014, 1123, 1076]),
        line(9, "AR4543924", [67, 1190, 396, 1256]),
        line(10, "الرقم العانلي ژماردى خيزاني", [980, 410, 1530, 500]),
        line(11, "1010E1876147874699", [60, 410, 940, 500]),
    ]

def cv_lines():
    return [
        line(0, "Synthetic Candidate"),
        line(1, "+964 770 123 4567"),
        line(2, "candidate.e2e@example.test"),
    ]

def passport_lines():
    return [
        line(0, "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<"),
        line(1, "L898902C36UTO7408122F1204159ZE184226B<<<<<10"),
    ]

def response(request_id, path):
    lowered = path.lower()
    if "nationalid_" in lowered:
        lines = national_id_lines()
        language = "ar"
    elif "passport_" in lowered:
        lines = passport_lines()
        language = "en"
    elif "cv_" in lowered:
        lines = cv_lines()
        language = "en"
    else:
        lines = [line(0, "Synthetic E2E document")]
        language = "en"

    return {
        "success": True,
        "requestId": request_id,
        "provider": "ZYNORA-E2E",
        "model": "DeterministicFixture",
        "language": language,
        "pages": [{"pageIndex": 0, "lines": lines}],
        "fullText": "\n".join(item["text"] for item in lines),
        "lineCount": len(lines),
        "errorType": None,
        "error": None,
    }

print(json.dumps({
    "ready": True,
    "provider": "ZYNORA-E2E",
    "model": "DeterministicFixture",
}), flush=True)

for raw in sys.stdin:
    raw = raw.lstrip("\ufeff").strip()
    if not raw:
        continue
    try:
        request = json.loads(raw)
        request_id = str(request.get("requestId") or "")
        path = str(request.get("path") or "")
        print(json.dumps(
            response(request_id, path),
            ensure_ascii=True,
        ), flush=True)
    except Exception as exc:
        print(json.dumps({
            "success": False,
            "requestId": locals().get("request_id"),
            "provider": "ZYNORA-E2E",
            "model": "DeterministicFixture",
            "language": None,
            "pages": [],
            "fullText": "",
            "lineCount": 0,
            "errorType": type(exc).__name__,
            "error": "deterministic test worker failure",
        }), flush=True)
