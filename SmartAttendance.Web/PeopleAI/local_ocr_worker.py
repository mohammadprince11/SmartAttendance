import argparse
import contextlib
import json
import os
import platform
import re
import sys
import tempfile
import traceback

# The worker protocol is JSON-over-stdio. Force UTF-8 on Windows pipes so
# Arabic OCR text and diagnostics can never fail with UnicodeEncodeError.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
os.environ.setdefault("FLAGS_minloglevel", "2")


class StartupStageError(RuntimeError):
    def __init__(self, error_code):
        super().__init__(error_code)
        self.error_code = error_code


def _read_runtime_config():
    requested_device = (
        os.environ.get("PEOPLE_AI_OCR_DEVICE", "auto").strip().lower()
        or "auto"
    )
    if requested_device not in {"auto", "cpu", "gpu"}:
        raise RuntimeError(
            "PEOPLE_AI_OCR_DEVICE must be one of: auto, cpu, gpu."
        )

    language = (
        os.environ.get("PEOPLE_AI_OCR_LANGUAGE", "ar").strip()
        or "ar"
    )

    configured_temp = os.environ.get(
        "PEOPLE_AI_OCR_TEMP_DIRECTORY", ""
    ).strip()
    temp_directory = configured_temp or tempfile.gettempdir()
    os.makedirs(temp_directory, exist_ok=True)

    handle, probe_path = tempfile.mkstemp(
        prefix="zynora-ocr-preflight-",
        dir=temp_directory,
    )
    os.close(handle)
    os.remove(probe_path)
    tempfile.tempdir = temp_directory

    configured_cache = os.environ.get(
        "PADDLE_PDX_CACHE_HOME", ""
    ).strip()
    if configured_cache:
        cache_directory = os.path.abspath(configured_cache)
        os.makedirs(cache_directory, exist_ok=True)
        handle, cache_probe = tempfile.mkstemp(
            prefix="zynora-paddlex-cache-probe-",
            dir=cache_directory,
        )
        os.close(handle)
        os.remove(cache_probe)

    return requested_device, language, temp_directory


def _verify_document_dependencies():
    try:
        from PIL import Image  # noqa: F401
        import pypdfium2  # noqa: F401
        import olefile  # noqa: F401
    except ImportError as exc:
        raise RuntimeError(
            "People AI document dependencies are incomplete. "
            "Install requirements-ocr.txt."
        ) from exc


def _resolve_device(requested_device):
    import paddle

    gpu_available = (
        paddle.device.is_compiled_with_cuda()
        and paddle.device.cuda.device_count() > 0
    )

    if requested_device == "gpu" and not gpu_available:
        raise RuntimeError(
            "GPU OCR was requested but the installed PaddlePaddle runtime "
            "does not expose an available CUDA device."
        )

    if requested_device == "auto":
        return "gpu" if gpu_available else "cpu"

    return requested_device


def _normalize_language_profile(value):
    raw = (value or "").replace(";", ",")
    languages = []
    for item in raw.split(","):
        code = item.strip().lower()
        if not code:
            continue
        if not re.fullmatch(r"[a-z0-9_-]{2,12}", code):
            raise RuntimeError(
                "Invalid OCR language code in language profile."
            )
        if code not in languages:
            languages.append(code)

    return languages or ["ar"]


def _build_ocr_engine(language, resolved_device):
    with contextlib.redirect_stdout(sys.stderr):
        from paddleocr import PaddleOCR
        return PaddleOCR(
            lang=language,
            device=resolved_device,
            use_doc_orientation_classify=True,
            use_doc_unwarping=False,
            use_textline_orientation=True,
        )


def build_ocr():
    try:
        requested_device, language, temp_directory = _read_runtime_config()
    except PermissionError as exc:
        raise StartupStageError(
            "STARTUP_RUNTIMECONFIG_PERMISSION_DENIED"
        ) from exc

    try:
        _verify_document_dependencies()
    except PermissionError as exc:
        raise StartupStageError(
            "STARTUP_DEPENDENCIES_PERMISSION_DENIED"
        ) from exc

    try:
        resolved_device = _resolve_device(requested_device)
    except PermissionError as exc:
        raise StartupStageError(
            "STARTUP_DEVICE_PERMISSION_DENIED"
        ) from exc

    try:
        _resolve_libreoffice_executable()
    except DocumentExtractionError as exc:
        raise StartupStageError(
            exc.error_code
        ) from exc

    default_languages = _normalize_language_profile(language)
    default_language = default_languages[0]

    try:
        ocr = _build_ocr_engine(default_language, resolved_device)
    except PermissionError as exc:
        raise StartupStageError(
            "STARTUP_MODELINIT_PERMISSION_DENIED"
        ) from exc

    diagnostics = {
        "provider": "PaddleOCR",
        "model": "PP-OCRv5",
        "language": default_language,
        "requestedDevice": requested_device,
        "device": resolved_device,
        "pythonVersion": platform.python_version(),
        "platform": platform.system().lower(),
        "tempWritable": True,
        "legacyOfficeConverterReady": True,
    }
    return ocr, diagnostics

class DocumentExtractionError(RuntimeError):
    def __init__(self, error_code, message):
        super().__init__(message)
        self.error_code = error_code


def to_plain(value):
    if value is None:
        return None
    if hasattr(value, "tolist"):
        return value.tolist()
    if isinstance(value, (str, int, float, bool)):
        return value
    if isinstance(value, dict):
        return {str(k): to_plain(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [to_plain(v) for v in value]
    return str(value)

@contextlib.contextmanager
def prepared_ocr_input(path):
    extension = os.path.splitext(path)[1].lower()
    if extension not in {".jpg", ".jpeg", ".png", ".webp"}:
        yield path
        return

    from PIL import Image, ImageOps

    max_side = int(os.environ.get("PEOPLE_AI_OCR_MAX_IMAGE_SIDE", "1600"))
    max_side = max(1200, min(max_side, 4096))
    temp_path = None

    try:
        with Image.open(path) as source:
            image = ImageOps.exif_transpose(source)
            original_size = image.size

            if image.mode != "RGB":
                image = image.convert("RGB")

            longest = max(image.size)
            if longest > max_side:
                scale = max_side / float(longest)
                target = (
                    max(1, round(image.width * scale)),
                    max(1, round(image.height * scale)),
                )
                image = image.resize(target, Image.Resampling.LANCZOS)

            handle, temp_path = tempfile.mkstemp(
                prefix="zynora-ocr-",
                suffix=".jpg",
            )
            os.close(handle)
            image.save(temp_path, "JPEG", quality=95, subsampling=0)

        print(
            f"OCR preprocessing dimensions: {original_size[0]}x{original_size[1]} "
            f"-> {image.size[0]}x{image.size[1]}",
            file=sys.stderr,
            flush=True,
        )
        yield temp_path
    finally:
        if temp_path:
            try:
                os.remove(temp_path)
            except OSError:
                pass


def _normalize_digits(value):
    table = str.maketrans({
        "٠":"0","١":"1","٢":"2","٣":"3","٤":"4",
        "٥":"5","٦":"6","٧":"7","٨":"8","٩":"9",
        "۰":"0","۱":"1","۲":"2","۳":"3","۴":"4",
        "۵":"5","۶":"6","۷":"7","۸":"8","۹":"9",
    })
    return (value or "").translate(table)


def _looks_like_family_label(value):
    compact = re.sub(r"\s+", "", (value or ""))
    return (
        "الرقمالعائلي" in compact
        or "الرقمالعانلي" in compact
        or "خيزاني" in compact
        or "خيزانى" in compact
    )


def _recover_family_number_line(ocr, prepared_path, results):
    if not results:
        return None

    first = results[0]
    texts = list(first.get("rec_texts", []) or [])
    boxes = to_plain(first.get("rec_boxes")) or []

    label_box = None
    for i, text in enumerate(texts):
        if _looks_like_family_label(str(text)):
            label_box = boxes[i] if i < len(boxes) else None
            break

    if not label_box or len(label_box) < 4:
        return None

    from PIL import Image, ImageOps, ImageEnhance, ImageFilter

    with Image.open(prepared_path) as source:
        image = ImageOps.exif_transpose(source).convert("RGB")
        width, height = image.size

        y1 = max(0, int(label_box[1]) - 70)
        y2 = min(height, int(label_box[3]) + 120)
        crop = image.crop((0, y1, width, y2))

        gray = ImageOps.grayscale(crop)
        contrast = ImageEnhance.Contrast(gray).enhance(2.4)
        sharp = contrast.filter(ImageFilter.SHARPEN)

        variants = [
            ("sharp", sharp.convert("RGB")),
            ("bw145", contrast.point(lambda p: 255 if p > 145 else 0).convert("RGB")),
            ("bw165", contrast.point(lambda p: 255 if p > 165 else 0).convert("RGB")),
        ]

        candidates = []

        for variant_name, variant in variants:
            scale = min(2.0, 4000.0 / max(variant.size))
            if scale > 1.0:
                variant = variant.resize(
                    (
                        max(1, round(variant.width * scale)),
                        max(1, round(variant.height * scale)),
                    ),
                    Image.Resampling.LANCZOS,
                )

            handle, temp_variant = tempfile.mkstemp(
                prefix=f"zynora-family-{variant_name}-",
                suffix=".jpg",
            )
            os.close(handle)

            try:
                variant.save(temp_variant, "JPEG", quality=97, subsampling=0)
                with contextlib.redirect_stdout(sys.stderr):
                    variant_results = list(ocr.predict(temp_variant))

                for item in variant_results:
                    variant_texts = list(item.get("rec_texts", []) or [])
                    variant_scores = list(item.get("rec_scores", []) or [])
                    variant_boxes = to_plain(item.get("rec_boxes")) or []

                    for i, raw_text in enumerate(variant_texts):
                        text = _normalize_digits(str(raw_text)).upper()
                        for match in re.finditer(
                            r"(?<![A-Z0-9])([A-Z0-9]{13,24})(?![A-Z0-9])",
                            text,
                        ):
                            candidate = match.group(1)
                            if sum(ch.isdigit() for ch in candidate) < 10:
                                continue
                            score = (
                                float(variant_scores[i])
                                if i < len(variant_scores)
                                else 0.0
                            )
                            box = (
                                variant_boxes[i]
                                if i < len(variant_boxes)
                                else None
                            )

                            mapped_box = None
                            if box and len(box) >= 4:
                                inv = 1.0 / scale
                                mapped_box = [
                                    round(box[0] * inv),
                                    round(box[1] * inv + y1),
                                    round(box[2] * inv),
                                    round(box[3] * inv + y1),
                                ]

                            candidates.append({
                                "text": candidate,
                                "score": score,
                                "box": mapped_box,
                                "variant": variant_name,
                            })
            finally:
                try:
                    os.remove(temp_variant)
                except OSError:
                    pass

        if not candidates:
            return None

        # Family numbers may be alphanumeric (example: 1010E1876147874699).
        # Preserve letters instead of stripping them as OCR noise.
        # The value still enters review as Pending and is never auto-approved.
        return sorted(
            candidates,
            key=lambda item: (len(item["text"]), item["score"]),
            reverse=True,
        )[0]


def _pdf_text_is_useful(text):
    normalized = re.sub(r"\s+", " ", (text or "").replace("\x00", " ")).strip()
    if len(normalized) < 40:
        return False
    return sum(ch.isalnum() for ch in normalized) >= 20


def _pdf_text_lines(text):
    lines = []
    for raw in (text or "").replace("\x00", " ").splitlines():
        value = re.sub(r"\s+", " ", raw).strip()
        if value:
            lines.append(value)
    if not lines:
        value = re.sub(r"\s+", " ", (text or "").replace("\x00", " ")).strip()
        if value:
            lines.append(value)
    return [
        {
            "index": index,
            "text": value,
            "score": 1.0,
            "box": None,
        }
        for index, value in enumerate(lines)
    ]


def _safe_close(resource):
    if resource is None:
        return
    try:
        close = getattr(resource, "close", None)
        if close:
            close()
    except Exception:
        pass


def _render_pdf_page_to_temp_image(page, dpi):
    bitmap = None
    temp_path = None
    try:
        bitmap = page.render(scale=dpi / 72.0)
        image = bitmap.to_pil().convert("RGB")
        handle, temp_path = tempfile.mkstemp(
            prefix="zynora-pdf-page-",
            suffix=".jpg",
        )
        os.close(handle)
        image.save(temp_path, "JPEG", quality=95, subsampling=0)
        return temp_path
    finally:
        _safe_close(bitmap)


def process_pdf_document(ocr, path, language):
    try:
        import pypdfium2 as pdfium
        pdf = pdfium.PdfDocument(path)
    except Exception as exc:
        message = str(exc)
        if "password" in message.casefold():
            raise DocumentExtractionError(
                "PDF_PASSWORD_PROTECTED",
                "Password-protected PDF requires manual review.",
            ) from exc
        raise DocumentExtractionError(
            "PDF_OPEN_FAILED",
            "PDF could not be opened safely.",
        ) from exc

    max_pages = int(os.environ.get("PEOPLE_AI_PDF_MAX_PAGES", "20"))
    max_pages = max(1, min(max_pages, 100))
    render_dpi = int(os.environ.get("PEOPLE_AI_PDF_RENDER_DPI", "180"))
    render_dpi = max(120, min(render_dpi, 300))

    page_count = len(pdf)
    if page_count <= 0:
        _safe_close(pdf)
        raise DocumentExtractionError(
            "PDF_EMPTY",
            "PDF does not contain any pages.",
        )
    if page_count > max_pages:
        _safe_close(pdf)
        raise DocumentExtractionError(
            "PDF_PAGE_LIMIT_EXCEEDED",
            f"PDF exceeds the configured {max_pages}-page processing limit.",
        )

    pages = []
    all_lines = []
    text_pages = 0
    ocr_pages = 0

    try:
        for page_index in range(page_count):
            page = None
            text_page = None
            temp_image = None
            try:
                page = pdf[page_index]
                text_page = page.get_textpage()
                direct_text = text_page.get_text_range() or ""

                if _pdf_text_is_useful(direct_text):
                    lines = _pdf_text_lines(direct_text)
                    text_pages += 1
                else:
                    temp_image = _render_pdf_page_to_temp_image(
                        page,
                        render_dpi,
                    )
                    image_result = process_file(
                        ocr,
                        temp_image,
                        language,
                    )
                    source_pages = image_result.get("pages", []) or []
                    lines = (
                        source_pages[0].get("lines", [])
                        if source_pages
                        else []
                    )
                    lines = [
                        {
                            "index": index,
                            "text": str(line.get("text", "")),
                            "score": line.get("score"),
                            "box": line.get("box"),
                        }
                        for index, line in enumerate(lines)
                        if str(line.get("text", "")).strip()
                    ]
                    ocr_pages += 1

                pages.append({
                    "pageIndex": page_index,
                    "lines": lines,
                })
                all_lines.extend(
                    str(line.get("text", ""))
                    for line in lines
                    if str(line.get("text", "")).strip()
                )
            finally:
                if temp_image:
                    try:
                        os.remove(temp_image)
                    except OSError:
                        pass
                _safe_close(text_page)
                _safe_close(page)
    finally:
        _safe_close(pdf)

    if text_pages == page_count:
        provider = "PDFium"
        model = "PDF-TextLayer-v1"
    elif ocr_pages == page_count:
        provider = "PaddleOCR"
        model = "PP-OCRv5-PDF"
    else:
        provider = "ZYNORA-PDF-Hybrid"
        model = "PDFium+PP-OCRv5"

    return {
        "success": True,
        "provider": provider,
        "model": model,
        "language": language,
        "pages": pages,
        "fullText": "\n".join(all_lines),
        "lineCount": len(all_lines),
    }


def _docx_limits():
    max_entries = int(os.environ.get("PEOPLE_AI_DOCX_MAX_ENTRIES", "2000"))
    max_entries = max(100, min(max_entries, 10000))

    max_uncompressed_mb = int(
        os.environ.get("PEOPLE_AI_DOCX_MAX_UNCOMPRESSED_MB", "64")
    )
    max_uncompressed_mb = max(8, min(max_uncompressed_mb, 512))

    max_ratio = int(
        os.environ.get("PEOPLE_AI_DOCX_MAX_COMPRESSION_RATIO", "200")
    )
    max_ratio = max(10, min(max_ratio, 1000))

    return max_entries, max_uncompressed_mb * 1024 * 1024, max_ratio


def _openxml_local_name(tag):
    return tag.rsplit("}", 1)[-1] if "}" in tag else tag


def _extract_openxml_lines(xml_bytes):
    import xml.etree.ElementTree as ET

    try:
        root = ET.fromstring(xml_bytes)
    except ET.ParseError as exc:
        raise DocumentExtractionError(
            "DOCX_XML_INVALID",
            "DOCX contains invalid Open XML content.",
        ) from exc

    lines = []
    for paragraph in root.iter():
        if _openxml_local_name(paragraph.tag) != "p":
            continue

        parts = []
        for node in paragraph.iter():
            name = _openxml_local_name(node.tag)
            if name == "t" and node.text:
                parts.append(node.text)
            elif name == "tab":
                parts.append(" ")
            elif name in {"br", "cr"}:
                parts.append(" ")

        value = re.sub(r"\s+", " ", "".join(parts)).strip()
        if value:
            lines.append(value)

    return lines


def process_docx_document(path, language):
    import zipfile

    max_entries, max_uncompressed, max_ratio = _docx_limits()

    try:
        archive = zipfile.ZipFile(path, "r")
    except (zipfile.BadZipFile, OSError) as exc:
        raise DocumentExtractionError(
            "DOCX_INVALID_PACKAGE",
            "DOCX package could not be opened safely.",
        ) from exc

    try:
        infos = archive.infolist()
        if len(infos) > max_entries:
            raise DocumentExtractionError(
                "DOCX_ENTRY_LIMIT_EXCEEDED",
                "DOCX contains too many archive entries.",
            )

        total_uncompressed = 0
        normalized_names = set()

        for info in infos:
            name = (info.filename or "").replace("\\", "/")
            lower_name = name.casefold()
            normalized_names.add(lower_name)

            parts = [part for part in name.split("/") if part]
            if name.startswith("/") or ".." in parts:
                raise DocumentExtractionError(
                    "DOCX_UNSAFE_PATH",
                    "DOCX contains an unsafe archive path.",
                )

            if info.flag_bits & 0x1:
                raise DocumentExtractionError(
                    "DOCX_ENCRYPTED",
                    "Encrypted DOCX requires manual review.",
                )

            total_uncompressed += max(0, info.file_size)
            if total_uncompressed > max_uncompressed:
                raise DocumentExtractionError(
                    "DOCX_UNCOMPRESSED_LIMIT_EXCEEDED",
                    "DOCX exceeds the configured uncompressed-size limit.",
                )

            if info.file_size >= 1024 * 1024:
                ratio = info.file_size / max(1, info.compress_size)
                if ratio > max_ratio:
                    raise DocumentExtractionError(
                        "DOCX_COMPRESSION_RATIO_EXCEEDED",
                        "DOCX compression ratio exceeds the safety limit.",
                    )

            if lower_name.endswith("vbaproject.bin"):
                raise DocumentExtractionError(
                    "DOCX_MACRO_CONTENT_UNSUPPORTED",
                    "Macro-enabled content is not accepted in DOCX extraction.",
                )

            if lower_name.startswith("word/embeddings/") and not name.endswith("/"):
                raise DocumentExtractionError(
                    "DOCX_EMBEDDED_OBJECT_UNSUPPORTED",
                    "Embedded objects require manual review.",
                )

        if "[content_types].xml" not in normalized_names:
            raise DocumentExtractionError(
                "DOCX_INVALID_PACKAGE",
                "DOCX content-types manifest is missing.",
            )

        if "word/document.xml" not in normalized_names:
            raise DocumentExtractionError(
                "DOCX_MAIN_DOCUMENT_MISSING",
                "DOCX main document part is missing.",
            )

        part_names = ["word/document.xml"]
        part_names.extend(sorted(
            info.filename
            for info in infos
            if re.fullmatch(
                r"word/(header|footer)\d+\.xml",
                (info.filename or "").replace("\\", "/"),
                flags=re.IGNORECASE,
            )
        ))

        text_lines = []
        for part_name in part_names:
            try:
                payload = archive.read(part_name)
            except (KeyError, RuntimeError) as exc:
                raise DocumentExtractionError(
                    "DOCX_PART_READ_FAILED",
                    "DOCX content part could not be read safely.",
                ) from exc

            text_lines.extend(_extract_openxml_lines(payload))
    finally:
        archive.close()

    lines = [
        {
            "index": index,
            "text": value,
            "score": 1.0,
            "box": None,
        }
        for index, value in enumerate(text_lines)
    ]

    return {
        "success": True,
        "provider": "OpenXML",
        "model": "DOCX-Text-v1",
        "language": language,
        "pages": [{"pageIndex": 0, "lines": lines}],
        "fullText": "\n".join(text_lines),
        "lineCount": len(text_lines),
    }


def _xlsx_limits():
    max_entries = int(os.environ.get("PEOPLE_AI_XLSX_MAX_ENTRIES", "5000"))
    max_entries = max(100, min(max_entries, 20000))

    max_uncompressed_mb = int(
        os.environ.get("PEOPLE_AI_XLSX_MAX_UNCOMPRESSED_MB", "64")
    )
    max_uncompressed_mb = max(8, min(max_uncompressed_mb, 512))

    max_ratio = int(
        os.environ.get("PEOPLE_AI_XLSX_MAX_COMPRESSION_RATIO", "200")
    )
    max_ratio = max(10, min(max_ratio, 1000))

    max_sheets = int(os.environ.get("PEOPLE_AI_XLSX_MAX_SHEETS", "50"))
    max_sheets = max(1, min(max_sheets, 200))

    max_rows = int(
        os.environ.get("PEOPLE_AI_XLSX_MAX_ROWS_PER_SHEET", "5000")
    )
    max_rows = max(100, min(max_rows, 100000))

    max_cells = int(
        os.environ.get("PEOPLE_AI_XLSX_MAX_CELLS_PER_SHEET", "50000")
    )
    max_cells = max(1000, min(max_cells, 500000))

    return (
        max_entries,
        max_uncompressed_mb * 1024 * 1024,
        max_ratio,
        max_sheets,
        max_rows,
        max_cells,
    )


def _xlsx_parse_xml(payload):
    import xml.etree.ElementTree as ET

    try:
        return ET.fromstring(payload)
    except ET.ParseError as exc:
        raise DocumentExtractionError(
            "XLSX_XML_INVALID",
            "XLSX contains invalid Open XML content.",
        ) from exc


def _xlsx_read_part(archive, name):
    try:
        return archive.read(name)
    except (KeyError, RuntimeError, OSError) as exc:
        raise DocumentExtractionError(
            "XLSX_PART_READ_FAILED",
            "XLSX content part could not be read safely.",
        ) from exc


def _xlsx_resolve_target(base_part, target):
    import posixpath

    value = (target or "").replace("\\", "/").strip()
    if not value:
        raise DocumentExtractionError(
            "XLSX_SHEET_RELATIONSHIP_MISSING",
            "XLSX sheet relationship target is missing.",
        )

    if value.startswith("/"):
        resolved = posixpath.normpath(value.lstrip("/"))
    else:
        resolved = posixpath.normpath(
            posixpath.join(posixpath.dirname(base_part), value)
        )

    if (
        resolved.startswith("../")
        or resolved.startswith("/")
        or not resolved.casefold().startswith("xl/")
    ):
        raise DocumentExtractionError(
            "XLSX_UNSAFE_PATH",
            "XLSX contains an unsafe relationship target.",
        )

    return resolved


def _xlsx_shared_strings(archive, name_map):
    part = name_map.get("xl/sharedstrings.xml")
    if not part:
        return []

    root = _xlsx_parse_xml(_xlsx_read_part(archive, part))
    values = []

    for item in root.iter():
        if _openxml_local_name(item.tag) != "si":
            continue
        pieces = [
            node.text or ""
            for node in item.iter()
            if _openxml_local_name(node.tag) == "t"
        ]
        values.append("".join(pieces))

    return values


def _xlsx_date_style_indexes(archive, name_map):
    part = name_map.get("xl/styles.xml")
    if not part:
        return set()

    root = _xlsx_parse_xml(_xlsx_read_part(archive, part))
    custom_formats = {}
    cell_xfs = None

    for node in root.iter():
        name = _openxml_local_name(node.tag)
        if name == "numFmt":
            try:
                num_fmt_id = int(node.attrib.get("numFmtId", "-1"))
            except ValueError:
                continue
            custom_formats[num_fmt_id] = node.attrib.get("formatCode", "")
        elif name == "cellXfs":
            cell_xfs = node

    built_in_dates = {
        14, 15, 16, 17, 18, 19, 20, 21, 22,
        27, 28, 29, 30, 31, 32, 33, 34, 35, 36,
        45, 46, 47, 50, 51, 52, 53, 54, 55, 56, 57, 58,
    }

    def is_date_format(num_fmt_id):
        if num_fmt_id in built_in_dates:
            return True

        code = custom_formats.get(num_fmt_id, "")
        if not code:
            return False

        cleaned = re.sub(r'"[^"]*"', "", code.casefold())
        cleaned = re.sub(r"\[[^\]]*\]", "", cleaned)
        cleaned = cleaned.replace("\\", "")
        return (
            "yy" in cleaned
            or "dd" in cleaned
            or ("h" in cleaned and "m" in cleaned)
            or ("h" in cleaned and "s" in cleaned)
        )

    date_indexes = set()
    if cell_xfs is not None:
        for index, xf in enumerate(list(cell_xfs)):
            if _openxml_local_name(xf.tag) != "xf":
                continue
            try:
                num_fmt_id = int(xf.attrib.get("numFmtId", "0"))
            except ValueError:
                num_fmt_id = 0
            if is_date_format(num_fmt_id):
                date_indexes.add(index)

    return date_indexes


def _xlsx_excel_serial(value, date1904):
    from datetime import datetime, timedelta

    try:
        serial = float(value)
    except (TypeError, ValueError):
        return value

    base = datetime(1904, 1, 1) if date1904 else datetime(1899, 12, 30)
    result = base + timedelta(days=serial)

    if result.time().hour or result.time().minute or result.time().second:
        return result.isoformat(timespec="seconds")
    return result.date().isoformat()


def _xlsx_cell_value(cell, shared_strings, date_styles, date1904):
    cell_type = (cell.attrib.get("t") or "").strip()
    style_index = -1

    try:
        style_index = int(cell.attrib.get("s", "-1"))
    except ValueError:
        pass

    value_node = None
    inline_node = None
    has_formula = False

    for child in list(cell):
        name = _openxml_local_name(child.tag)
        if name == "v":
            value_node = child
        elif name == "is":
            inline_node = child
        elif name == "f":
            has_formula = True

    raw = value_node.text if value_node is not None else None

    if cell_type == "inlineStr" and inline_node is not None:
        raw = "".join(
            node.text or ""
            for node in inline_node.iter()
            if _openxml_local_name(node.tag) == "t"
        )
    elif cell_type == "s":
        try:
            index = int(raw or "-1")
        except ValueError:
            index = -1
        raw = (
            shared_strings[index]
            if 0 <= index < len(shared_strings)
            else ""
        )
    elif cell_type == "b":
        raw = "TRUE" if raw == "1" else "FALSE"
    elif cell_type == "d":
        raw = raw or ""
    elif has_formula and raw is None:
        # Never execute formulas. A formula without a cached value contributes
        # no extracted value and remains available in the protected source file.
        return ""

    if raw is None:
        return ""

    value = str(raw).replace("\x00", " ").strip()
    if style_index in date_styles and cell_type not in {"s", "inlineStr", "str"}:
        value = _xlsx_excel_serial(value, date1904)

    value = re.sub(r"\s+", " ", value).strip()
    return value[:4096]


def process_xlsx_document(path, language):
    import zipfile

    (
        max_entries,
        max_uncompressed,
        max_ratio,
        max_sheets,
        max_rows,
        max_cells,
    ) = _xlsx_limits()

    try:
        archive = zipfile.ZipFile(path, "r")
    except (zipfile.BadZipFile, OSError) as exc:
        raise DocumentExtractionError(
            "XLSX_INVALID_PACKAGE",
            "XLSX package could not be opened safely.",
        ) from exc

    try:
        infos = archive.infolist()
        if len(infos) > max_entries:
            raise DocumentExtractionError(
                "XLSX_ENTRY_LIMIT_EXCEEDED",
                "XLSX contains too many archive entries.",
            )

        total_uncompressed = 0
        name_map = {}

        for info in infos:
            name = (info.filename or "").replace("\\", "/")
            lower_name = name.casefold()
            name_map[lower_name] = name

            parts = [part for part in name.split("/") if part]
            if name.startswith("/") or ".." in parts:
                raise DocumentExtractionError(
                    "XLSX_UNSAFE_PATH",
                    "XLSX contains an unsafe archive path.",
                )

            if info.flag_bits & 0x1:
                raise DocumentExtractionError(
                    "XLSX_ENCRYPTED",
                    "Encrypted XLSX requires manual review.",
                )

            total_uncompressed += max(0, info.file_size)
            if total_uncompressed > max_uncompressed:
                raise DocumentExtractionError(
                    "XLSX_UNCOMPRESSED_LIMIT_EXCEEDED",
                    "XLSX exceeds the configured uncompressed-size limit.",
                )

            if info.file_size >= 1024 * 1024:
                ratio = info.file_size / max(1, info.compress_size)
                if ratio > max_ratio:
                    raise DocumentExtractionError(
                        "XLSX_COMPRESSION_RATIO_EXCEEDED",
                        "XLSX compression ratio exceeds the safety limit.",
                    )

            if lower_name.endswith("vbaproject.bin"):
                raise DocumentExtractionError(
                    "XLSX_MACRO_CONTENT_UNSUPPORTED",
                    "Macro-enabled content is not accepted in XLSX extraction.",
                )

            if lower_name.startswith("xl/embeddings/") and not name.endswith("/"):
                raise DocumentExtractionError(
                    "XLSX_EMBEDDED_OBJECT_UNSUPPORTED",
                    "Embedded spreadsheet objects require manual review.",
                )

        required = (
            "[content_types].xml",
            "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels",
        )
        for required_name in required:
            if required_name not in name_map:
                raise DocumentExtractionError(
                    "XLSX_INVALID_PACKAGE",
                    "XLSX required Open XML parts are missing.",
                )

        workbook_root = _xlsx_parse_xml(
            _xlsx_read_part(archive, name_map["xl/workbook.xml"])
        )
        rels_root = _xlsx_parse_xml(
            _xlsx_read_part(
                archive,
                name_map["xl/_rels/workbook.xml.rels"],
            )
        )

        date1904 = False
        sheets = []
        for node in workbook_root.iter():
            local_name = _openxml_local_name(node.tag)
            if local_name == "workbookPr":
                date1904 = (
                    str(node.attrib.get("date1904", "")).casefold()
                    in {"1", "true"}
                )
            elif local_name == "sheet":
                rel_id = next(
                    (
                        value
                        for key, value in node.attrib.items()
                        if _openxml_local_name(key) == "id"
                    ),
                    None,
                )
                sheets.append((
                    re.sub(
                        r"\s+",
                        " ",
                        node.attrib.get("name", "Sheet"),
                    ).strip()[:256],
                    rel_id,
                ))

        if len(sheets) > max_sheets:
            raise DocumentExtractionError(
                "XLSX_SHEET_LIMIT_EXCEEDED",
                "XLSX contains too many worksheets.",
            )

        relationships = {}
        for node in rels_root.iter():
            if _openxml_local_name(node.tag) != "Relationship":
                continue
            rel_id = node.attrib.get("Id")
            target = node.attrib.get("Target")
            target_mode = (node.attrib.get("TargetMode") or "").casefold()
            rel_type = node.attrib.get("Type") or ""
            if (
                rel_id
                and target
                and target_mode != "external"
                and rel_type.casefold().endswith("/worksheet")
            ):
                relationships[rel_id] = _xlsx_resolve_target(
                    "xl/workbook.xml",
                    target,
                )

        shared_strings = _xlsx_shared_strings(archive, name_map)
        date_styles = _xlsx_date_style_indexes(archive, name_map)

        pages = []
        full_text_lines = []

        for page_index, (sheet_name, rel_id) in enumerate(sheets):
            if not rel_id or rel_id not in relationships:
                continue

            part_name = relationships[rel_id]
            actual_name = name_map.get(part_name.casefold())
            if not actual_name:
                raise DocumentExtractionError(
                    "XLSX_SHEET_PART_MISSING",
                    "XLSX worksheet part is missing.",
                )

            sheet_root = _xlsx_parse_xml(
                _xlsx_read_part(archive, actual_name)
            )

            lines = [{
                "index": 0,
                "text": f"Sheet: {sheet_name or 'Sheet'}",
                "score": 1.0,
                "box": None,
            }]
            row_count = 0
            cell_count = 0

            for row in sheet_root.iter():
                if _openxml_local_name(row.tag) != "row":
                    continue

                row_count += 1
                if row_count > max_rows:
                    raise DocumentExtractionError(
                        "XLSX_ROW_LIMIT_EXCEEDED",
                        "XLSX worksheet exceeds the row-processing limit.",
                    )

                pieces = []
                for cell in list(row):
                    if _openxml_local_name(cell.tag) != "c":
                        continue

                    cell_count += 1
                    if cell_count > max_cells:
                        raise DocumentExtractionError(
                            "XLSX_CELL_LIMIT_EXCEEDED",
                            "XLSX worksheet exceeds the cell-processing limit.",
                        )

                    value = _xlsx_cell_value(
                        cell,
                        shared_strings,
                        date_styles,
                        date1904,
                    )
                    if not value:
                        continue

                    reference = (cell.attrib.get("r") or "").upper()
                    if not re.fullmatch(r"[A-Z]{1,4}[0-9]{1,7}", reference):
                        reference = f"CELL{cell_count}"

                    pieces.append(f"{reference}={value}")

                if not pieces:
                    continue

                row_number = row.attrib.get("r") or str(row_count)
                for offset in range(0, len(pieces), 20):
                    chunk = pieces[offset:offset + 20]
                    line_text = (
                        f"Row {row_number}: " + " | ".join(chunk)
                    )[:16000]
                    lines.append({
                        "index": len(lines),
                        "text": line_text,
                        "score": 1.0,
                        "box": None,
                    })

            pages.append({
                "pageIndex": page_index,
                "lines": lines,
            })
            full_text_lines.extend(
                line["text"]
                for line in lines
                if line["text"]
            )
    finally:
        archive.close()

    return {
        "success": True,
        "provider": "OpenXML",
        "model": "XLSX-Cells-v1",
        "language": language,
        "pages": pages,
        "fullText": "\n".join(full_text_lines),
        "lineCount": len(full_text_lines),
    }


def _resolve_libreoffice_executable():
    import shutil

    configured = os.environ.get(
        "PEOPLE_AI_LIBREOFFICE_EXECUTABLE",
        "",
    ).strip()

    candidates = [
        configured,
        shutil.which("soffice"),
        shutil.which("libreoffice"),
    ]

    if os.name == "nt":
        candidates.extend([
            os.path.join(
                os.environ.get("ProgramFiles", r"C:\Program Files"),
                "LibreOffice",
                "program",
                "soffice.exe",
            ),
            os.path.join(
                os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
                "LibreOffice",
                "program",
                "soffice.exe",
            ),
        ])

    for candidate in candidates:
        if candidate and os.path.isfile(candidate):
            return os.path.abspath(candidate)

    raise DocumentExtractionError(
        "LEGACY_OFFICE_CONVERTER_UNAVAILABLE",
        "LibreOffice converter is not available for legacy Office extraction.",
    )


def _reject_legacy_office_macros(path):
    import olefile

    try:
        if not olefile.isOleFile(path):
            raise DocumentExtractionError(
                "LEGACY_OFFICE_INVALID_PACKAGE",
                "Legacy Office file is not a valid OLE compound document.",
            )

        with olefile.OleFileIO(path) as ole:
            entries = ole.listdir(streams=True, storages=True)
    except DocumentExtractionError:
        raise
    except Exception as exc:
        raise DocumentExtractionError(
            "LEGACY_OFFICE_INVALID_PACKAGE",
            "Legacy Office file could not be inspected safely.",
        ) from exc

    for entry in entries:
        names = [str(part).casefold() for part in entry]
        joined = "/".join(names)
        if (
            "vba" in names
            or "macros" in names
            or "_vba_project" in joined
            or "projectwm" in joined
            or "vba_project" in joined
        ):
            raise DocumentExtractionError(
                "LEGACY_OFFICE_MACRO_CONTENT_UNSUPPORTED",
                "Legacy Office files containing macros require manual review.",
            )


def process_legacy_office_document(path, language):
    import pathlib
    import shutil
    import subprocess

    extension = os.path.splitext(path)[1].lower()
    if extension not in {".doc", ".xls"}:
        raise DocumentExtractionError(
            "LEGACY_OFFICE_FORMAT_UNSUPPORTED",
            "Legacy Office conversion only supports DOC and XLS.",
        )

    _reject_legacy_office_macros(path)
    executable = _resolve_libreoffice_executable()

    timeout_seconds = int(os.environ.get(
        "PEOPLE_AI_OFFICE_CONVERSION_TIMEOUT_SECONDS",
        "90",
    ))
    timeout_seconds = max(15, min(timeout_seconds, 300))

    target_extension = ".docx" if extension == ".doc" else ".xlsx"
    convert_format = "docx" if extension == ".doc" else "xlsx"

    with tempfile.TemporaryDirectory(
        prefix="zynora-legacy-office-",
        dir=tempfile.gettempdir(),
    ) as work_dir:
        source_path = os.path.join(work_dir, "source" + extension)
        output_dir = os.path.join(work_dir, "out")
        profile_dir = os.path.join(work_dir, "lo-profile")
        os.makedirs(output_dir, exist_ok=True)
        os.makedirs(profile_dir, exist_ok=True)
        shutil.copyfile(path, source_path)

        profile_uri = pathlib.Path(profile_dir).resolve().as_uri()
        command = [
            executable,
            "--headless",
            "--nologo",
            "--nodefault",
            "--nofirststartwizard",
            "--norestore",
            "--nolockcheck",
            f"-env:UserInstallation={profile_uri}",
            "--convert-to",
            convert_format,
            "--outdir",
            output_dir,
            source_path,
        ]

        environment = os.environ.copy()
        environment["SAL_USE_VCLPLUGIN"] = "svp"

        creation_flags = (
            getattr(subprocess, "CREATE_NO_WINDOW", 0)
            if os.name == "nt"
            else 0
        )

        try:
            completed = subprocess.run(
                command,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                timeout=timeout_seconds,
                check=False,
                env=environment,
                creationflags=creation_flags,
            )
        except subprocess.TimeoutExpired as exc:
            raise DocumentExtractionError(
                "LEGACY_OFFICE_CONVERSION_TIMEOUT",
                "Legacy Office conversion exceeded the configured timeout.",
            ) from exc
        except OSError as exc:
            raise DocumentExtractionError(
                "LEGACY_OFFICE_CONVERTER_UNAVAILABLE",
                "LibreOffice converter could not be started.",
            ) from exc

        if completed.returncode != 0:
            raise DocumentExtractionError(
                "LEGACY_OFFICE_CONVERSION_FAILED",
                "Legacy Office conversion failed.",
            )

        converted_path = os.path.join(
            output_dir,
            "source" + target_extension,
        )
        if not os.path.isfile(converted_path):
            matches = [
                item
                for item in os.listdir(output_dir)
                if item.casefold().endswith(target_extension)
            ]
            if len(matches) == 1:
                converted_path = os.path.join(output_dir, matches[0])

        if not os.path.isfile(converted_path):
            raise DocumentExtractionError(
                "LEGACY_OFFICE_OUTPUT_MISSING",
                "Legacy Office conversion did not produce the expected output.",
            )

        if extension == ".doc":
            result = process_docx_document(converted_path, language)
            result["provider"] = "LibreOffice+OpenXML"
            result["model"] = "DOC-via-DOCX-v1"
        else:
            result = process_xlsx_document(converted_path, language)
            result["provider"] = "LibreOffice+OpenXML"
            result["model"] = "XLS-via-XLSX-v1"

        return result


def process_file(ocr, path, language):
    if not os.path.isfile(path):
        raise FileNotFoundError(path)

    extension = os.path.splitext(path)[1].lower()
    if extension == ".pdf":
        return process_pdf_document(ocr, path, language)
    if extension == ".doc":
        return process_legacy_office_document(path, language)
    if extension == ".docx":
        return process_docx_document(path, language)
    if extension == ".xls":
        return process_legacy_office_document(path, language)
    if extension == ".xlsx":
        return process_xlsx_document(path, language)

    family_number_recovery = None
    with prepared_ocr_input(path) as prepared_path:
        with contextlib.redirect_stdout(sys.stderr):
            results = list(ocr.predict(prepared_path))

        if language == "ar":
            family_number_recovery = _recover_family_number_line(
                ocr,
                prepared_path,
                results,
            )

    pages = []
    all_lines = []

    for page_index, item in enumerate(results):
        rec_texts = list(item.get("rec_texts", []) or [])
        rec_scores = list(item.get("rec_scores", []) or [])
        rec_boxes = item.get("rec_boxes")
        boxes = to_plain(rec_boxes) or []

        lines = []
        for index, text in enumerate(rec_texts):
            score = float(rec_scores[index]) if index < len(rec_scores) else None
            box = boxes[index] if index < len(boxes) else None
            line = {
                "index": index,
                "text": str(text),
                "score": score,
                "box": box,
            }
            lines.append(line)
            all_lines.append(str(text))

        if page_index == 0 and family_number_recovery:
            recovered = {
                "index": len(lines),
                "text": family_number_recovery["text"],
                "score": family_number_recovery["score"],
                "box": family_number_recovery["box"],
            }
            lines.append(recovered)
            all_lines.append(family_number_recovery["text"])
            print(
                "Recovered FamilyNumber candidate "
                f"via {family_number_recovery['variant']} "
                f"(score={family_number_recovery['score']:.4f})",
                file=sys.stderr,
                flush=True,
            )

        pages.append({
            "pageIndex": page_index,
            "lines": lines,
        })

    return {
        "success": True,
        "provider": "PaddleOCR",
        "model": "PP-OCRv5",
        "language": language,
        "pages": pages,
        "fullText": "\n".join(all_lines),
        "lineCount": len(all_lines),
    }


def merge_ocr_results(results, language_profile):
    if len(results) == 1:
        single = results[0]
        single["language"] = language_profile
        return single

    pages_by_index = {}
    seen_text = set()

    for result in results:
        for page in result.get("pages", []) or []:
            page_index = int(page.get("pageIndex", 0))
            target = pages_by_index.setdefault(page_index, [])
            for line in page.get("lines", []) or []:
                text = str(line.get("text", "")).strip()
                key = re.sub(r"\s+", " ", text).casefold()
                if not key or key in seen_text:
                    continue
                seen_text.add(key)
                target.append({
                    "index": len(target),
                    "text": text,
                    "score": line.get("score"),
                    "box": line.get("box"),
                })

    pages = [
        {"pageIndex": index, "lines": pages_by_index[index]}
        for index in sorted(pages_by_index)
    ]
    all_lines = [
        line["text"]
        for page in pages
        for line in page["lines"]
    ]

    providers = {
        str(result.get("provider", "")).strip()
        for result in results
        if str(result.get("provider", "")).strip()
    }
    models = {
        str(result.get("model", "")).strip()
        for result in results
        if str(result.get("model", "")).strip()
    }

    return {
        "success": True,
        "provider": (
            next(iter(providers))
            if len(providers) == 1
            else "ZYNORA-MultiLanguage"
        ),
        "model": (
            next(iter(models))
            if len(models) == 1
            else "+".join(sorted(models))
        ),
        "language": language_profile,
        "pages": pages,
        "fullText": "\n".join(all_lines),
        "lineCount": len(all_lines),
    }


def serve():
    try:
        ocr, diagnostics = build_ocr()
    except Exception as exc:
        print(json.dumps({
            "ready": False,
            "errorType": getattr(
                exc,
                "error_code",
                type(exc).__name__,
            ),
            "error": str(exc),
        }, ensure_ascii=True), flush=True)
        traceback.print_exc(file=sys.stderr)
        raise

    ocr_cache = {
        diagnostics["language"]: ocr,
    }

    ready = {"ready": True, **diagnostics}
    print(json.dumps(ready, ensure_ascii=True), flush=True)

    for raw in sys.stdin:
        # .NET/other callers may accidentally prefix the first stdin message
        # with a UTF-8 BOM. Ignore it defensively; the canonical C# client also
        # emits BOM-free UTF-8.
        raw = raw.lstrip("\ufeff").strip()
        if not raw:
            continue

        try:
            request = json.loads(raw)
            request_id = request.get("requestId")
            path = request.get("path")
            requested_profile = (
                request.get("language")
                or diagnostics["language"]
            )
            languages = _normalize_language_profile(
                requested_profile
            )
            profile = ",".join(languages)
            extension = os.path.splitext(path or "")[1].lower()

            if extension in {".doc", ".xls"}:
                result = process_legacy_office_document(path, profile)
            elif extension == ".docx":
                result = process_docx_document(path, profile)
            elif extension == ".xlsx":
                result = process_xlsx_document(path, profile)
            else:
                outputs = []
                for language in languages:
                    engine = ocr_cache.get(language)
                    if engine is None:
                        engine = _build_ocr_engine(
                            language,
                            diagnostics["device"],
                        )
                        ocr_cache[language] = engine

                    outputs.append(
                        process_file(engine, path, language)
                    )

                result = merge_ocr_results(outputs, profile)
            result["requestId"] = request_id
            print(json.dumps(result, ensure_ascii=True), flush=True)
        except Exception as exc:
            print(json.dumps({
                "success": False,
                "requestId": request.get("requestId") if "request" in locals() else None,
                "errorType": getattr(
                    exc,
                    "error_code",
                    type(exc).__name__,
                ),
                "error": str(exc),
            }, ensure_ascii=True), flush=True)
            traceback.print_exc(file=sys.stderr)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--serve", action="store_true")
    parser.add_argument("--preflight", action="store_true")
    parser.add_argument("--input")
    args = parser.parse_args()

    if args.serve:
        serve()
        return

    if args.input:
        extension = os.path.splitext(args.input)[1].lower()
        if extension in {".doc", ".docx", ".xls", ".xlsx"}:
            language = (
                os.environ.get("PEOPLE_AI_OCR_LANGUAGE", "ar").strip()
                or "ar"
            )
            if extension in {".doc", ".xls"}:
                result = process_legacy_office_document(
                    args.input,
                    language,
                )
            elif extension == ".docx":
                result = process_docx_document(args.input, language)
            else:
                result = process_xlsx_document(args.input, language)
            print(json.dumps(result, ensure_ascii=True))
            return

    ocr, diagnostics = build_ocr()

    if args.preflight:
        print(json.dumps(
            {"ready": True, **diagnostics},
            ensure_ascii=True,
        ))
        return

    if not args.input:
        raise SystemExit(
            "--input is required when --serve/--preflight is not used"
        )

    result = process_file(
        ocr,
        args.input,
        diagnostics["language"],
    )
    print(json.dumps(result, ensure_ascii=True))

if __name__ == "__main__":
    main()
