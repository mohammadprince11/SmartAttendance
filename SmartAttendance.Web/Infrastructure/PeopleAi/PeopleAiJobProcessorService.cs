using System.Diagnostics;
using System.Text.RegularExpressions;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.PeopleAi;

public sealed class PeopleAiJobProcessorService : BackgroundService
{
    private const string ExtractorVersion = "local-document-worker-v2";
    private const string SchemaVersion = "people-ai-extraction-v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILocalOcrProcessClient _ocr;
    private readonly IWebHostEnvironment _environment;
    private readonly PeopleAiWorkerOptions _options;
    private readonly ILogger<PeopleAiJobProcessorService> _logger;
    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public PeopleAiJobProcessorService(
        IServiceScopeFactory scopeFactory,
        ILocalOcrProcessClient ocr,
        IWebHostEnvironment environment,
        Microsoft.Extensions.Options.IOptions<PeopleAiWorkerOptions> options,
        ILogger<PeopleAiJobProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _ocr = ocr;
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.IsUsable || !_ocr.IsEnabled)
        {
            _logger.LogInformation(
                "People AI local worker is disabled.");
            return;
        }

        var preflightStage = "ProtectedStorage";

        try
        {
            var protectedRoot =
                OnboardingProtectedAssetService.ResolveRoot(
                    _environment.ContentRootPath);
            Directory.CreateDirectory(protectedRoot);

            var probePath = Path.Combine(
                protectedRoot,
                $".people-ai-worker-probe-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(
                probePath,
                "zynora",
                stoppingToken);
            File.Delete(probePath);

            preflightStage = "LocalOcrHandshake";
            await _ocr.EnsureReadyAsync(stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            var startupErrorCode = MapStartupErrorCode(exception);

            if (_environment.IsDevelopment())
            {
                _logger.LogCritical(
                    exception,
                    "People AI OCR startup preflight failed. Stage={Stage} Error={ErrorCode}. Queue processing will not start.",
                    preflightStage,
                    startupErrorCode);
            }
            else
            {
                _logger.LogCritical(
                    "People AI OCR startup preflight failed. Stage={Stage} Error={ErrorCode}. Queue processing will not start.",
                    preflightStage,
                    startupErrorCode);
            }

            return;
        }

        _logger.LogInformation(
            "People AI local worker started on {WorkerId} using device {Device} and language {Language}.",
            _workerId,
            _options.Device,
            _options.Language);

        var idleDelay = TimeSpan.FromSeconds(
            Math.Clamp(_options.PollSeconds, 1, 30));
        // A hard process termination cannot release the SQL lock marker.
        // Recover it shortly after the configured OCR timeout instead of
        // leaving a phantom "Processing" job in the queue indefinitely.
        var staleAfter = TimeSpan.FromSeconds(
            Math.Max(90, _options.JobTimeoutSeconds + 30));
        var recoveryInterval = TimeSpan.FromSeconds(
            Math.Clamp(_options.PollSeconds * 5, 5, 30));
        var nextRecoveryAt = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            EmployeeOnboardingStore.JobRow? job = null;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider
                    .GetRequiredService<ApplicationDbContext>();

                var utcNow = DateTime.UtcNow;
                if (utcNow >= nextRecoveryAt)
                {
                    await EmployeeOnboardingStore.RecoverStaleJobsAsync(
                        db,
                        staleAfter);
                    nextRecoveryAt = utcNow.Add(recoveryInterval);
                }

                job = await EmployeeOnboardingStore.ClaimNextJobAsync(
                    db,
                    _workerId);

                if (job is null)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(db, job, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (_environment.IsDevelopment())
                {
                    _logger.LogError(
                        exception,
                        "People AI worker cycle failed for job {JobId}.",
                        job?.Id);
                }
                else
                {
                    _logger.LogError(
                        "People AI worker cycle failed for job {JobId}.",
                        job?.Id);
                }

                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(
        ApplicationDbContext db,
        EmployeeOnboardingStore.JobRow job,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                job.JobType,
                "ProcessOnboardingDocument",
                StringComparison.Ordinal) ||
            job.OnboardingDocumentId is not > 0)
        {
            await EmployeeOnboardingStore.FailJobAsync(
                db,
                job.Id,
                "UNSUPPORTED_JOB",
                TimeSpan.Zero);
            return;
        }

        var input = await PeopleAiExtractionStore.GetProcessingInputAsync(
            db,
            job.OnboardingDocumentId.Value);

        if (input is null)
        {
            await EmployeeOnboardingStore.FailJobAsync(
                db,
                job.Id,
                "DOCUMENT_NOT_FOUND",
                TimeSpan.Zero);
            return;
        }

        var contract = DocumentProcessingContract.Resolve(
            Path.GetExtension(input.StorageKey),
            input.DeclaredDocumentType);

        if (!contract.ShouldQueueAutomaticExtraction)
        {
            await PeopleAiExtractionStore.CreateManualReviewRunAsync(
                db,
                input.DocumentId,
                input.SessionId,
                input.CompanyId,
                input.DeclaredDocumentType);

            var unsupportedSeverity =
                DocumentProcessingContract.HasStructuredExtractorDefinition(
                    input.DeclaredDocumentType)
                    ? "Blocking"
                    : "Warning";

            await EmployeeOnboardingStore
                .MarkDocumentStoredWithoutExtractionAsync(
                    db,
                    job.Id,
                    input.DocumentId,
                    input.SessionId,
                    unsupportedSeverity);
            _logger.LogInformation(
                "Skipped automatic extraction for document {DocumentId}; mode={ProcessingMode}.",
                input.DocumentId,
                contract.ProcessingMode);
            return;
        }

        var root = OnboardingProtectedAssetService.ResolveRoot(
            _environment.ContentRootPath);

        if (!ProtectedFileStore.TryResolvePhysicalPath(
                root,
                input.StorageKey,
                out var physicalPath) ||
            !File.Exists(physicalPath))
        {
            await EmployeeOnboardingStore.FailJobAsync(
                db,
                job.Id,
                "PROTECTED_FILE_NOT_FOUND",
                TimeSpan.Zero);
            await PeopleAiExtractionStore.FailAsync(
                db,
                input,
                null,
                "PROTECTED_FILE_NOT_FOUND",
                willRetry: false);
            return;
        }

        long? runId = null;
        var stopwatch = Stopwatch.StartNew();
        var (extractionProvider, extractionModel) =
            contract.Format.Extension.ToLowerInvariant() switch
            {
                ".pdf" => (
                    "ZYNORA-PDF-Hybrid",
                    "PDFium+PP-OCRv5"),
                ".doc" => (
                    "LibreOffice+OpenXML",
                    "DOC-via-DOCX-v1"),
                ".docx" => (
                    "OpenXML",
                    "DOCX-Text-v1"),
                ".xls" => (
                    "LibreOffice+OpenXML",
                    "XLS-via-XLSX-v1"),
                ".xlsx" => (
                    "OpenXML",
                    "XLSX-Cells-v1"),
                _ => (
                    "PaddleOCR",
                    "PP-OCRv5-Mobile")
            };

        try
        {
            runId = await PeopleAiExtractionStore.StartRunAsync(
                db,
                input,
                extractionProvider,
                extractionModel,
                ExtractorVersion,
                SchemaVersion);

            var companyPolicy =
                await PeopleAiSettingsStore.GetAsync(
                    db,
                    input.CompanyId);
            var languageProfile =
                DocumentProcessingContract.ResolveOcrLanguageProfile(
                    companyPolicy.EnabledLanguages,
                    input.DeclaredDocumentType,
                    _options.Language);

            var response = await _ocr.ExtractAsync(
                physicalPath,
                languageProfile,
                input.DeclaredDocumentType,
                cancellationToken);

            if (!response.Success)
            {
                throw new LocalOcrException(
                    response.ErrorType ?? "LOCAL_OCR_FAILED");
            }

            await PeopleAiExtractionStore.SaveOcrResultAsync(
                db,
                runId.Value,
                response);

            var detectedType = await SaveDeterministicExtractionAsync(
                db,
                input,
                runId.Value,
                response);

            await PeopleAiExtractionStore.CompleteAsync(
                db,
                input,
                runId.Value,
                detectedType);

            await EmployeeOnboardingStore.CompleteJobAsync(
                db,
                job.Id);

            stopwatch.Stop();
            await PeopleAiExtractionStore.RecordAuditAsync(
                db,
                input,
                "DocumentProcessed",
                response.Provider,
                response.Model,
                success: true,
                (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            var errorCode = MapErrorCode(exception);
            var willRetry =
                !IsPermanentDocumentFailure(errorCode) &&
                job.AttemptCount < job.MaxAttempts;

            await EmployeeOnboardingStore.FailJobAsync(
                db,
                job.Id,
                errorCode,
                willRetry
                    ? RetryDelay(job.AttemptCount)
                    : TimeSpan.Zero);

            await PeopleAiExtractionStore.FailAsync(
                db,
                input,
                runId,
                errorCode,
                willRetry);

            await PeopleAiExtractionStore.RecordAuditAsync(
                db,
                input,
                "DocumentProcessed",
                extractionProvider,
                extractionModel,
                success: false,
                (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                errorCode);

            if (_environment.IsDevelopment())
            {
                _logger.LogWarning(
                    exception,
                    "People AI document processing failed for document {DocumentId}; retry={WillRetry}; error={ErrorCode}.",
                    input.DocumentId,
                    willRetry,
                    errorCode);
            }
            else
            {
                _logger.LogWarning(
                    "People AI document processing failed for document {DocumentId}; retry={WillRetry}; error={ErrorCode}.",
                    input.DocumentId,
                    willRetry,
                    errorCode);
            }
        }
    }

    private static TimeSpan RetryDelay(int attemptCount) =>
        TimeSpan.FromSeconds(
            Math.Min(300, Math.Max(5, attemptCount * attemptCount * 5)));

    private static string MapStartupErrorCode(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return "OCR_STARTUP_TIMEOUT";
        }

        if (exception is FileNotFoundException)
        {
            return "STARTUP_FILE_NOT_FOUND";
        }

        if (exception is UnauthorizedAccessException)
        {
            return "STARTUP_ACCESS_DENIED";
        }

        if (exception is IOException)
        {
            return "STARTUP_IO_ERROR";
        }

        if (exception is InvalidOperationException invalid)
        {
            var runtimeMatch = Regex.Match(
                invalid.Message,
                @"startup failed \((?<type>[A-Za-z0-9_]{1,80})\):",
                RegexOptions.CultureInvariant);

            if (runtimeMatch.Success)
            {
                return "OCR_RUNTIME_" +
                    NormalizeErrorCode(runtimeMatch.Groups["type"].Value);
            }

            if (invalid.Message.Contains(
                    "exited during startup",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "OCR_PROCESS_EXITED";
            }

            if (invalid.Message.Contains(
                    "Failed to start local OCR process",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "OCR_PROCESS_START_FAILED";
            }

            return "STARTUP_INVALID_OPERATION";
        }

        return "STARTUP_" +
            NormalizeErrorCode(exception.GetType().Name);
    }

    private static string MapErrorCode(Exception exception) =>
        exception switch
        {
            TimeoutException => "LOCAL_OCR_TIMEOUT",
            OperationCanceledException => "LOCAL_OCR_CANCELLED",
            FileNotFoundException => "PROTECTED_FILE_NOT_FOUND",
            LocalOcrException local => NormalizeErrorCode(local.Code),
            _ => "LOCAL_OCR_PROCESSING_ERROR"
        };

    private static bool IsPermanentDocumentFailure(
        string errorCode) =>
        errorCode is
            "PDF_PASSWORD_PROTECTED" or
            "PDF_PAGE_LIMIT_EXCEEDED" or
            "PDF_EMPTY" or
            "PDF_OPEN_FAILED" or
            "LEGACY_OFFICE_INVALID_PACKAGE" or
            "LEGACY_OFFICE_MACRO_CONTENT_UNSUPPORTED" or
            "LEGACY_OFFICE_CONVERTER_UNAVAILABLE" or
            "LEGACY_OFFICE_CONVERSION_FAILED" or
            "LEGACY_OFFICE_OUTPUT_MISSING" or
            "LEGACY_OFFICE_FORMAT_UNSUPPORTED" or
            "DOCX_INVALID_PACKAGE" or
            "DOCX_ENTRY_LIMIT_EXCEEDED" or
            "DOCX_UNCOMPRESSED_LIMIT_EXCEEDED" or
            "DOCX_COMPRESSION_RATIO_EXCEEDED" or
            "DOCX_ENCRYPTED" or
            "DOCX_UNSAFE_PATH" or
            "DOCX_MACRO_CONTENT_UNSUPPORTED" or
            "DOCX_EMBEDDED_OBJECT_UNSUPPORTED" or
            "DOCX_MAIN_DOCUMENT_MISSING" or
            "DOCX_XML_INVALID" or
            "DOCX_PART_READ_FAILED" or
            "XLSX_INVALID_PACKAGE" or
            "XLSX_ENTRY_LIMIT_EXCEEDED" or
            "XLSX_UNCOMPRESSED_LIMIT_EXCEEDED" or
            "XLSX_COMPRESSION_RATIO_EXCEEDED" or
            "XLSX_ENCRYPTED" or
            "XLSX_UNSAFE_PATH" or
            "XLSX_MACRO_CONTENT_UNSUPPORTED" or
            "XLSX_EMBEDDED_OBJECT_UNSUPPORTED" or
            "XLSX_XML_INVALID" or
            "XLSX_SHEET_LIMIT_EXCEEDED" or
            "XLSX_ROW_LIMIT_EXCEEDED" or
            "XLSX_CELL_LIMIT_EXCEEDED" or
            "XLSX_SHEET_RELATIONSHIP_MISSING" or
            "XLSX_SHEET_PART_MISSING" or
            "XLSX_PART_READ_FAILED";

    private static string NormalizeErrorCode(string value)
    {
        var code = new string((value ?? string.Empty)
            .ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Take(80)
            .ToArray());

        return code.Length == 0
            ? "LOCAL_OCR_FAILED"
            : code;
    }

    private static async Task<PeopleAiExtractionStore.DocumentClassification>
        SaveDeterministicExtractionAsync(
        ApplicationDbContext db,
        PeopleAiExtractionStore.ProcessingInput input,
        long runId,
        LocalOcrResponse response)
    {
        var mrz = TryParseMrz(response);
        var declaredType = string.IsNullOrWhiteSpace(
            input.DeclaredDocumentType)
            ? PeopleAiDocumentTypes.Unknown
            : input.DeclaredDocumentType!;

        var isDeclaredPassport = string.Equals(
            declaredType,
            PeopleAiDocumentTypes.Passport,
            StringComparison.OrdinalIgnoreCase);
        var passportVisual = isDeclaredPassport
            ? PassportVisualParser.Parse(
                response.AllLines.Select(line =>
                    new PassportVisualOcrLine(
                        line.Text,
                        line.Score,
                        line.Box)))
            : null;
        var hasStrongPassportVisualEvidence =
            passportVisual?.HasStrongIdentityEvidence == true;

        if (string.Equals(
                declaredType,
                PeopleAiDocumentTypes.Cv,
                StringComparison.OrdinalIgnoreCase))
        {
            var cvContact = CvContactParser.Parse(
                response.AllLines.Select(line => line.Text));
            var cv = CvIntelligenceParser.Parse(
                response.AllLines.Select(line =>
                    new CvIntelligenceLine(
                        line.Text,
                        line.Score)));

            await SaveCvField(
                "FullName",
                cv.FullName);
            await SaveCvField(
                "Phone",
                cvContact.Phone);
            await SaveCvField(
                "PersonalEmail",
                cvContact.Email);
            await SaveCvField(
                "Address",
                cv.Address);
            await SaveCvField(
                "Nationality",
                cv.Nationality);
            await SaveCvField(
                "Skills",
                cv.Skills.Count == 0
                    ? null
                    : string.Join("; ", cv.Skills));
            await SaveCvField(
                "Languages",
                cv.Languages.Count == 0
                    ? null
                    : string.Join("; ", cv.Languages));

            await PeopleAiStructuredRecordStore.ReplaceForRunAsync(
                db,
                input.SessionId,
                input.DocumentId,
                runId,
                cv.Records);
        }

        var nationalId = IraqiNationalIdParser.Parse(
            response.AllLines.Select(line =>
                new IraqiNationalIdOcrLine(
                    line.Text,
                    line.Score,
                    line.Box)));

        var hasStrongNationalIdEvidence =
            !string.IsNullOrWhiteSpace(nationalId.NationalNumber) &&
            (
                !string.IsNullOrWhiteSpace(nationalId.FamilyNumber) ||
                (
                    !string.IsNullOrWhiteSpace(nationalId.FirstName) &&
                    !string.IsNullOrWhiteSpace(nationalId.SecondName)
                )
            );

        if (string.Equals(
                declaredType,
                PeopleAiDocumentTypes.NationalId,
                StringComparison.OrdinalIgnoreCase) ||
            hasStrongNationalIdEvidence)
        {
            await SaveObservedField(
                "NationalNumber",
                nationalId.NationalNumber);
            await SaveObservedField(
                "DocumentNumber",
                nationalId.DocumentNumber);
            await SaveObservedField(
                "FamilyNumber",
                nationalId.FamilyNumber);
            await SaveObservedField(
                "FirstName",
                nationalId.FirstName);
            await SaveObservedField(
                "SecondName",
                nationalId.SecondName);
            await SaveObservedField(
                "ThirdName",
                nationalId.ThirdName);
            await SaveObservedField(
                "LastName",
                nationalId.LastName);
            await SaveObservedField(
                "MotherName",
                nationalId.MotherName);
            if (mrz is null)
            {
                await SaveObservedField(
                    "Sex",
                    nationalId.Sex);
            }
        }

        if (isDeclaredPassport && passportVisual is not null)
        {
            if (mrz is null)
            {
                await SaveObservedField(
                    "DocumentNumber",
                    passportVisual.DocumentNumber);
                await SaveObservedField(
                    "GivenNames",
                    passportVisual.GivenNames);
                await SaveObservedField(
                    "Surname",
                    passportVisual.Surname);
                await SaveObservedField(
                    "DateOfBirth",
                    passportVisual.DateOfBirth);
                await SaveObservedField(
                    "ExpiryDate",
                    passportVisual.ExpiryDate);
                await SaveObservedField(
                    "Nationality",
                    passportVisual.Nationality);
                await SaveObservedField(
                    "Sex",
                    passportVisual.Sex);
                await SaveObservedField(
                    "IssuingCountry",
                    passportVisual.IssuingCountry);
            }

            await SaveObservedField(
                "IssueDate",
                passportVisual.IssueDate);
            await SaveObservedField(
                "PlaceOfBirth",
                passportVisual.PlaceOfBirth);
            await SaveObservedField(
                "MotherName",
                passportVisual.MotherName);
            await SaveObservedField(
                "IssuingAuthority",
                passportVisual.IssuingAuthority);
        }

        if (mrz is null)
        {
            if (isDeclaredPassport)
            {
                await PeopleAiExtractionStore.AddValidationIssueAsync(
                    db,
                    input.SessionId,
                    input.DocumentId,
                    "PASSPORT_MRZ_NOT_FOUND",
                    "MRZ",
                    "Warning",
                    "MRZ",
                    hasStrongPassportVisualEvidence
                        ? "لم يتم العثور على MRZ صالح، لكن تم استخراج بيانات الجواز المرئية. يرجى التحقق منها أثناء المراجعة."
                        : "لم يتم العثور على MRZ صالح في الجواز. يجب مراجعة المستند يدوياً.");
            }

            if (hasStrongPassportVisualEvidence)
            {
                return new PeopleAiExtractionStore.DocumentClassification(
                    PeopleAiDocumentTypes.Passport,
                    0.82m,
                    "PASSPORT_VISUAL_LABELS");
            }

            return hasStrongNationalIdEvidence
                ? new PeopleAiExtractionStore.DocumentClassification(
                    PeopleAiDocumentTypes.NationalId,
                    0.90m,
                    "IRAQI_NATIONAL_ID_RULES")
                : new PeopleAiExtractionStore.DocumentClassification(
                    null,
                    null,
                    null);
        }

        var validation = mrz.AllRequiredChecksValid
            ? "Valid"
            : "Invalid";
        var mrzConfidence =
            mrz.AllRequiredChecksValid ? 0.99 : 0.85;

        await SaveField(
            "MRZ.Format",
            mrz.Format,
            mrz.Format,
            "Valid");
        await SaveField(
            "DocumentNumber",
            mrz.DocumentNumber,
            IdentityDocumentNormalizer.NormalizeNumber(
                mrz.DocumentNumber),
            validation);
        var normalizedNationality =
            NormalizeMrzAlphaCode(mrz.Nationality);
        await SaveField(
            "Nationality",
            mrz.Nationality,
            normalizedNationality,
            validation);
        await SaveField(
            "DateOfBirth",
            mrz.DateOfBirth?.ToString("yyyy-MM-dd"),
            mrz.DateOfBirth?.ToString("yyyy-MM-dd"),
            validation);
        await SaveField(
            "ExpiryDate",
            mrz.ExpiryDate?.ToString("yyyy-MM-dd"),
            mrz.ExpiryDate?.ToString("yyyy-MM-dd"),
            validation);
        await SaveField(
            "Sex",
            mrz.Sex,
            mrz.Sex,
            validation);
        if (HasReliableMrzNames(mrz))
        {
            await SaveField(
                "Surname",
                mrz.Surname,
                mrz.Surname,
                validation);
            await SaveField(
                "GivenNames",
                string.Join(' ', mrz.GivenNames),
                string.Join(' ', mrz.GivenNames),
                validation);
        }

        await SaveField(
            "IssuingCountry",
            mrz.IssuingCountry,
            NormalizeMrzAlphaCode(mrz.IssuingCountry),
            validation);

        foreach (var check in mrz.Checks)
        {
            await SaveField(
                $"MRZ.Check.{check.Field}",
                check.IsValid ? "Valid" : "Invalid",
                check.IsValid ? "Valid" : "Invalid",
                check.IsValid ? "Valid" : "Invalid");

            if (!check.IsValid)
            {
                await PeopleAiExtractionStore.AddValidationIssueAsync(
                    db,
                    input.SessionId,
                    input.DocumentId,
                    $"MRZ_{check.Field.ToUpperInvariant()}_CHECK_FAILED",
                    "MRZ",
                    "Blocking",
                    check.Field,
                    $"فشل تحقق MRZ للحقل {check.Field}. لا يعتمد الحقل قبل المراجعة.");
            }
        }

        if (string.Equals(
                mrz.Format,
                "TD3",
                StringComparison.OrdinalIgnoreCase))
        {
            return new PeopleAiExtractionStore.DocumentClassification(
                PeopleAiDocumentTypes.Passport,
                mrz.AllRequiredChecksValid ? 0.99m : 0.85m,
                "MRZ_TD3");
        }

        if (hasStrongNationalIdEvidence)
        {
            return new PeopleAiExtractionStore.DocumentClassification(
                PeopleAiDocumentTypes.NationalId,
                0.90m,
                "IRAQI_NATIONAL_ID_RULES");
        }

        return new PeopleAiExtractionStore.DocumentClassification(
            null,
            null,
            mrz is null ? null : $"MRZ_{mrz.Format}");

        double? ResolveObservedConfidence(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var needle = value.Trim();
            var canonicalNeedle = new string(
                needle
                    .Where(char.IsLetterOrDigit)
                    .Select(char.ToUpperInvariant)
                    .ToArray());

            var scores = response.AllLines
                .Where(line =>
                {
                    if (!line.Score.HasValue ||
                        string.IsNullOrWhiteSpace(line.Text))
                    {
                        return false;
                    }

                    var lineText = line.Text.Trim();
                    if (lineText.Contains(
                            needle,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (canonicalNeedle.Length < 4)
                    {
                        return false;
                    }

                    var canonicalLine = new string(
                        lineText
                            .Where(char.IsLetterOrDigit)
                            .Select(char.ToUpperInvariant)
                            .ToArray());

                    return canonicalLine.Contains(
                        canonicalNeedle,
                        StringComparison.Ordinal);
                })
                .Select(line => line.Score!.Value)
                .ToList();

            return scores.Count == 0
                ? null
                : scores.Max();
        }

        Task SaveObservedField(
            string fieldKey,
            string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? Task.CompletedTask
                : PeopleAiExtractionStore.SaveSemanticFieldAsync(
                    db,
                    runId,
                    fieldKey,
                    value,
                    value,
                    ResolveObservedConfidence(value),
                    "Observed",
                    "OCR_LABEL");

        Task SaveCvField(
            string fieldKey,
            string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? Task.CompletedTask
                : PeopleAiExtractionStore.SaveSemanticFieldAsync(
                    db,
                    runId,
                    fieldKey,
                    value,
                    value,
                    ResolveObservedConfidence(value),
                    "Observed",
                    "CV_REGEX");

        Task SaveField(
            string fieldKey,
            string? raw,
            string? normalized,
            string fieldValidation) =>
            PeopleAiExtractionStore.SaveSemanticFieldAsync(
                db,
                runId,
                fieldKey,
                raw,
                normalized,
                mrzConfidence,
                fieldValidation,
                "MRZ");
    }

    private sealed record CvContact(
        string? Phone,
        string? Email);

    private static CvContact ExtractCvContact(
        LocalOcrResponse response)
    {
        string? email = null;
        string? phone = null;

        foreach (var line in response.AllLines)
        {
            var text = (line.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (email is null)
            {
                var emailMatch = Regex.Match(
                    text,
                    @"[A-Z0-9._%+-]+@[A-Z0-9.-]+.[A-Z]{2,}",
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant);

                if (emailMatch.Success)
                {
                    email = emailMatch.Value.Trim();
                }
            }

            if (phone is null)
            {
                var phoneMatch = Regex.Match(
                    text,
                    @"[+]?[0-9][0-9 ()-]{8,}[0-9]",
                    RegexOptions.CultureInvariant);

                if (phoneMatch.Success)
                {
                    var raw = phoneMatch.Value.Trim();
                    var digits = new string(
                        raw.Where(char.IsDigit).ToArray());

                    if (digits.Length is >= 10 and <= 15)
                    {
                        phone = raw.StartsWith(
                                "+",
                                StringComparison.Ordinal)
                            ? "+" + digits
                            : digits;
                    }
                }
            }

            if (phone is not null &&
                email is not null)
            {
                break;
            }
        }

        return new CvContact(phone, email);
    }

    private static MrzParseResult? TryParseMrz(
        LocalOcrResponse response) =>
        MrzOcrParser.Parse(
            response.AllLines.Select(line => line.Text));

    private static bool HasReliableMrzNames(
        MrzParseResult mrz)
    {
        if (string.IsNullOrWhiteSpace(mrz.Surname) ||
            mrz.GivenNames.Count == 0)
        {
            return false;
        }

        var nameLine = mrz.Format switch
        {
            "TD3" when mrz.RawLines.Count >= 1 =>
                mrz.RawLines[0][5..],
            "TD1" when mrz.RawLines.Count >= 3 =>
                mrz.RawLines[2],
            _ => string.Empty
        };

        return nameLine.Contains("<<", StringComparison.Ordinal) &&
               nameLine.All(c => c == '<' ||
                                 c is >= 'A' and <= 'Z');
    }

    private static string NormalizeMrzAlphaCode(string? value)
    {
        var code = (value ?? string.Empty)
            .Trim()
            .ToUpperInvariant();

        if (code.Length != 3)
        {
            return code;
        }

        return new string(code.Select(c => c switch
        {
            '1' => 'I',
            '0' => 'O',
            _ => c
        }).ToArray());
    }

    private static string NormalizeMrzLine(string? value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '<')
            .ToArray());

    private sealed class LocalOcrException(string code) :
        Exception(code)
    {
        public string Code { get; } = code;
    }
}
