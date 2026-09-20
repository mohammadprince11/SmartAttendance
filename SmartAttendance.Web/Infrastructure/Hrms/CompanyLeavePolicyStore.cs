using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Company-scoped leave and timed-permission policy engine.</summary>
public static class CompanyLeavePolicyStore
{
    public sealed class Policy
    {
        public int CompanyId { get; set; }
        public int RequestTypeId { get; set; }
        public string RequestTypeName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public bool NeedsTime { get; set; }
        public bool RequiresBalance { get; set; }
        public decimal? EntitlementAmount { get; set; }
        public string BalanceUnit { get; set; } = "Days";
        public bool AllowNegative { get; set; }
        public decimal? NegativeLimitAmount { get; set; }
        public int? BalanceSourceRequestTypeId { get; set; }
        public string BalanceSourceName { get; set; } = string.Empty;
        public decimal HoursPerDayOverride { get; set; }
        public decimal? MaxPerRequestAmount { get; set; }
        public decimal? MaxPerYearAmount { get; set; }
        public int MinimumNoticeDays { get; set; }
        public int EligibilityDays { get; set; }
        public bool CarryForwardEnabled { get; set; }
        public decimal? CarryForwardMaxAmount { get; set; }
        public int? CarryForwardExpiryMonths { get; set; }
        public string AccrualMethod { get; set; } = "FullUpfront";
        public bool ProrateOnHire { get; set; }
        public bool AllowRetroactive { get; set; }
        public bool ReasonRequired { get; set; }
        public bool? AttachmentRequiredOverride { get; set; }
        public bool IsConfigured { get; set; }
    }

    public sealed record ValidationResult(bool Ok, string Message, decimal Entitlement = 0m,
        decimal Reserved = 0m, decimal Requested = 0m, decimal RemainingAfter = 0m, string Unit = "Days");

    public sealed record BalanceSnapshot(
        int SourceRequestTypeId,
        string RequestTypeName,
        string CategoryName,
        decimal Entitlement,
        decimal Reserved,
        decimal Remaining,
        string Unit);

    private sealed record StoredPolicy(int RequestTypeId, bool RequiresBalance, decimal? EntitlementAmount,
        bool AllowNegative, int? BalanceSourceRequestTypeId, decimal HoursPerDayOverride, string BalanceUnit,
        decimal? NegativeLimitAmount, decimal? MaxPerRequestAmount, decimal? MaxPerYearAmount,
        int MinimumNoticeDays, int EligibilityDays, bool CarryForwardEnabled, decimal? CarryForwardMaxAmount,
        int? CarryForwardExpiryMonths, string AccrualMethod, bool ProrateOnHire, bool AllowRetroactive,
        bool ReasonRequired, bool? AttachmentRequiredOverride);

    private sealed record UsageRow(int Id, int? RequestTypeId, string RequestType, DateOnly FromDate,
        DateOnly ToDate, TimeSpan? StartTime, TimeSpan? EndTime);
    private sealed record EmployeeFacts(int CompanyId, DateOnly? HireDate);

    public static async Task<List<Policy>> ListForCompanyAsync(ApplicationDbContext db, int companyId, bool onlyActive = true)
    {
        await RequestTypeStore.EnsureAsync(db);
        var types = await RequestTypeStore.ListTypesAsync(db, onlyActive: onlyActive);
        var eligible = types.Where(type =>
        {
            var effect = BulkRequestStore.ResolveEffect(type);
            return effect.Kind is BulkRequestStore.EffectKind.Leave or BulkRequestStore.EffectKind.ExitPermission;
        }).ToList();
        var stored = await LoadStoredAsync(db, companyId);
        var byId = stored.ToDictionary(row => row.RequestTypeId);
        var names = types.ToDictionary(type => type.Id, type => type.Name);
        return eligible.Select(type => BuildEffective(companyId, type, byId, names))
            .OrderBy(x => x.CategoryName).ThenBy(x => x.RequestTypeName).ToList();
    }

    public static async Task SaveAsync(ApplicationDbContext db, Policy policy, string actor)
    {
        if (policy.CompanyId <= 0 || policy.RequestTypeId <= 0)
            throw new InvalidOperationException("الشركة ونوع الطلب مطلوبان.");
        if (policy.EntitlementAmount is < 0 || policy.NegativeLimitAmount is < 0 ||
            policy.MaxPerRequestAmount is < 0 || policy.MaxPerYearAmount is < 0 || policy.CarryForwardMaxAmount is < 0)
            throw new InvalidOperationException("قيم الرصيد والحدود لا يمكن أن تكون سالبة.");
        if (policy.HoursPerDayOverride < 0 || policy.HoursPerDayOverride > 24)
            throw new InvalidOperationException("ساعات اليوم يجب أن تكون بين 0 و24؛ الصفر يعني شفت الموظف.");
        if (policy.MinimumNoticeDays < 0 || policy.EligibilityDays < 0)
            throw new InvalidOperationException("أيام الإشعار والاستحقاق لا يمكن أن تكون سالبة.");

        var unit = NormalizeUnit(policy.BalanceUnit);
        var accrual = NormalizeAccrual(policy.AccrualMethod);
        var source = policy.BalanceSourceRequestTypeId is > 0 ? policy.BalanceSourceRequestTypeId : null;
        await HrmsDatabase.ExecuteAsync(db, """
MERGE CompanyLeavePolicies AS target
USING (SELECT @CompanyId CompanyId,@RequestTypeId RequestTypeId) source
ON target.CompanyId=source.CompanyId AND target.RequestTypeId=source.RequestTypeId
WHEN MATCHED THEN UPDATE SET
 RequiresBalance=@RequiresBalance,EntitlementAmount=@EntitlementAmount,AllowNegative=@AllowNegative,
 BalanceSourceRequestTypeId=@BalanceSource,HoursPerDayOverride=@HoursOverride,BalanceUnit=@BalanceUnit,
 NegativeLimitAmount=@NegativeLimit,MaxPerRequestAmount=@MaxPerRequest,MaxPerYearAmount=@MaxPerYear,
 MinimumNoticeDays=@MinimumNoticeDays,EligibilityDays=@EligibilityDays,CarryForwardEnabled=@CarryForwardEnabled,
 CarryForwardMaxAmount=@CarryForwardMax,CarryForwardExpiryMonths=@CarryForwardExpiry,AccrualMethod=@AccrualMethod,
 ProrateOnHire=@ProrateOnHire,AllowRetroactive=@AllowRetroactive,ReasonRequired=@ReasonRequired,
 AttachmentRequiredOverride=@AttachmentRequiredOverride,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@Actor
WHEN NOT MATCHED THEN INSERT
 (CompanyId,RequestTypeId,RequiresBalance,EntitlementAmount,AllowNegative,BalanceSourceRequestTypeId,
 HoursPerDayOverride,BalanceUnit,NegativeLimitAmount,MaxPerRequestAmount,MaxPerYearAmount,MinimumNoticeDays,
 EligibilityDays,CarryForwardEnabled,CarryForwardMaxAmount,CarryForwardExpiryMonths,AccrualMethod,ProrateOnHire,
 AllowRetroactive,ReasonRequired,AttachmentRequiredOverride,CreatedAt,CreatedBy)
VALUES
 (@CompanyId,@RequestTypeId,@RequiresBalance,@EntitlementAmount,@AllowNegative,@BalanceSource,@HoursOverride,
 @BalanceUnit,@NegativeLimit,@MaxPerRequest,@MaxPerYear,@MinimumNoticeDays,@EligibilityDays,@CarryForwardEnabled,
 @CarryForwardMax,@CarryForwardExpiry,@AccrualMethod,@ProrateOnHire,@AllowRetroactive,@ReasonRequired,
 @AttachmentRequiredOverride,SYSUTCDATETIME(),@Actor);
""", command =>
        {
            HrmsDatabase.AddParameter(command,"@CompanyId",policy.CompanyId);
            HrmsDatabase.AddParameter(command,"@RequestTypeId",policy.RequestTypeId);
            HrmsDatabase.AddParameter(command,"@RequiresBalance",policy.RequiresBalance);
            HrmsDatabase.AddParameter(command,"@EntitlementAmount",(object?)policy.EntitlementAmount ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@AllowNegative",policy.AllowNegative);
            HrmsDatabase.AddParameter(command,"@BalanceSource",(object?)source ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@HoursOverride",policy.HoursPerDayOverride);
            HrmsDatabase.AddParameter(command,"@BalanceUnit",unit);
            HrmsDatabase.AddParameter(command,"@NegativeLimit",(object?)policy.NegativeLimitAmount ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@MaxPerRequest",(object?)policy.MaxPerRequestAmount ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@MaxPerYear",(object?)policy.MaxPerYearAmount ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@MinimumNoticeDays",policy.MinimumNoticeDays);
            HrmsDatabase.AddParameter(command,"@EligibilityDays",policy.EligibilityDays);
            HrmsDatabase.AddParameter(command,"@CarryForwardEnabled",policy.CarryForwardEnabled);
            HrmsDatabase.AddParameter(command,"@CarryForwardMax",(object?)policy.CarryForwardMaxAmount ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@CarryForwardExpiry",(object?)policy.CarryForwardExpiryMonths ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@AccrualMethod",accrual);
            HrmsDatabase.AddParameter(command,"@ProrateOnHire",policy.ProrateOnHire);
            HrmsDatabase.AddParameter(command,"@AllowRetroactive",policy.AllowRetroactive);
            HrmsDatabase.AddParameter(command,"@ReasonRequired",policy.ReasonRequired);
            HrmsDatabase.AddParameter(command,"@AttachmentRequiredOverride",(object?)policy.AttachmentRequiredOverride ?? DBNull.Value);
            HrmsDatabase.AddParameter(command,"@Actor",actor);
        });
    }

    public sealed record RequestSlice(DateOnly FromDate, DateOnly ToDate, TimeSpan? StartTime = null, TimeSpan? EndTime = null);

    public static Task<ValidationResult> ValidateRequestAsync(ApplicationDbContext db, int employeeId,
        int currentRequestId, int? requestTypeId, string requestTypeName, DateOnly fromDate, DateOnly toDate,
        TimeSpan? startTime, TimeSpan? endTime, string? reason = null, bool hasAttachment = false) =>
        ValidateRequestSetAsync(db, employeeId, currentRequestId, requestTypeId, requestTypeName,
            new[] { new RequestSlice(fromDate, toDate, startTime, endTime) }, reason, hasAttachment);

    public static async Task<ValidationResult> ValidateRequestSetAsync(ApplicationDbContext db, int employeeId,
        int currentRequestId, int? requestTypeId, string requestTypeName, IReadOnlyList<RequestSlice> requests,
        string? reason = null, bool hasAttachment = false)
    {
        if (requests.Count == 0) return new(true, string.Empty);
        if (requests.Any(x => x.ToDate < x.FromDate))
            return new(false, "تاريخ نهاية الطلب يسبق تاريخ البداية.");

        await RequestTypeStore.EnsureAsync(db);
        var facts = await LoadEmployeeFactsAsync(db, employeeId);
        if (facts.CompanyId <= 0) return new(true, string.Empty);

        var types = await RequestTypeStore.ListTypesAsync(db, onlyActive: false);
        var type = requestTypeId is > 0
            ? types.FirstOrDefault(x => x.Id == requestTypeId.Value)
            : types.FirstOrDefault(x => string.Equals(x.Name, requestTypeName, StringComparison.OrdinalIgnoreCase));
        if (type is null) return new(true, string.Empty);

        var policies = await BuildPolicyMapAsync(db, facts.CompanyId, types);
        if (!policies.TryGetValue(type.Id, out var policy)) return new(true, string.Empty);

        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var request in requests.OrderBy(x => x.FromDate))
        {
            if (!policy.AllowRetroactive && request.FromDate < today)
                return new(false, "سياسة الشركة لا تسمح بتقديم هذا النوع بأثر رجعي.");
            if (policy.MinimumNoticeDays > 0 && request.FromDate >= today &&
                request.FromDate.DayNumber - today.DayNumber < policy.MinimumNoticeDays)
                return new(false, $"هذا النوع يحتاج إشعاراً مسبقاً لا يقل عن {policy.MinimumNoticeDays} يوم.");
            if (policy.EligibilityDays > 0 && facts.HireDate is { } hire &&
                request.FromDate.DayNumber - hire.DayNumber < policy.EligibilityDays)
                return new(false, $"الموظف لم يكمل مدة الاستحقاق المطلوبة ({policy.EligibilityDays} يوم خدمة).");
        }

        if (policy.ReasonRequired && string.IsNullOrWhiteSpace(reason))
            return new(false, "سبب الطلب إلزامي حسب سياسة الشركة.");
        var attachmentRequired = policy.AttachmentRequiredOverride ?? type.AttachmentRequired;
        if (attachmentRequired && !hasAttachment)
            return new(false, "هذا الطلب يتطلب مرفقاً حسب سياسة الشركة.");
        if (!policy.RequiresBalance) return new(true, string.Empty);

        var sourceId = policy.BalanceSourceRequestTypeId ?? policy.RequestTypeId;
        if (!policies.TryGetValue(sourceId, out var sourcePolicy)) sourcePolicy = policy;

        decimal grandRequested = 0m;
        decimal lastEntitlement = 0m;
        decimal lastReserved = 0m;
        decimal lastRemaining = 0m;

        foreach (var yearGroup in ExpandAcrossYears(requests).GroupBy(x => x.FromDate.Year).OrderBy(g => g.Key))
        {
            var usage = await LoadUsageAsync(db, employeeId, currentRequestId, yearGroup.Key);
            var existingReserved = await SumUsageForSourceAsync(db, employeeId, facts.CompanyId, sourceId, usage, policies, types);
            decimal hypothetical = 0m;

            foreach (var request in yearGroup.OrderBy(x => x.FromDate))
            {
                var requested = await CalculateDebitAsync(db, employeeId, facts.CompanyId,
                    request.FromDate, request.ToDate, request.StartTime, request.EndTime,
                    sourcePolicy.BalanceUnit, policy.HoursPerDayOverride);

                if (policy.MaxPerRequestAmount is { } maxRequest && requested > maxRequest)
                    return new(false,
                        $"الطلب يتجاوز الحد الأعلى للطلب الواحد ({maxRequest:0.##} {UnitLabel(sourcePolicy.BalanceUnit)}).",
                        Requested: requested, Unit: sourcePolicy.BalanceUnit);

                var entitlement = CalculateAccruedEntitlement(sourcePolicy, facts.HireDate, request.FromDate);
                entitlement += await CalculateCarryForwardAsync(db, employeeId, facts, sourcePolicy,
                    sourceId, policies, types, request.FromDate);
                var yearlyCap = policy.MaxPerYearAmount ?? sourcePolicy.MaxPerYearAmount;
                var projectedUsed = existingReserved + hypothetical + requested;
                if (yearlyCap is { } maxYear && projectedUsed > maxYear)
                    return new(false,
                        $"الاستخدام السنوي سيتجاوز الحد ({maxYear:0.##} {UnitLabel(sourcePolicy.BalanceUnit)}).",
                        entitlement, existingReserved + hypothetical, requested,
                        entitlement - projectedUsed, sourcePolicy.BalanceUnit);

                var remainingAfter = entitlement - projectedUsed;
                var minAllowed = sourcePolicy.AllowNegative
                    ? -(sourcePolicy.NegativeLimitAmount ?? decimal.MaxValue)
                    : 0m;
                if (remainingAfter < minAllowed)
                {
                    var sourceName = string.IsNullOrWhiteSpace(sourcePolicy.RequestTypeName)
                        ? "الرصيد المحدد"
                        : sourcePolicy.RequestTypeName;
                    return new(false,
                        $"الرصيد غير كافٍ في «{sourceName}». المتاح قبل الطلب {entitlement - existingReserved - hypothetical:0.##} {UnitLabel(sourcePolicy.BalanceUnit)}، والطلب يحتاج {requested:0.##}.",
                        entitlement, existingReserved + hypothetical, requested,
                        remainingAfter, sourcePolicy.BalanceUnit);
                }

                hypothetical += requested;
                grandRequested += requested;
                lastEntitlement = entitlement;
                lastReserved = existingReserved;
                lastRemaining = remainingAfter;
            }
        }

        return new(true, string.Empty, lastEntitlement, lastReserved, grandRequested,
            lastRemaining, sourcePolicy.BalanceUnit);
    }

    public static async Task<List<BalanceSnapshot>> GetBalanceSnapshotsAsync(
        ApplicationDbContext db,
        int employeeId,
        DateOnly asOf)
    {
        var facts = await LoadEmployeeFactsAsync(db, employeeId);
        if (facts.CompanyId <= 0) return new();

        await RequestTypeStore.EnsureAsync(db);
        var types = await RequestTypeStore.ListTypesAsync(db, onlyActive: false);
        var policies = await BuildPolicyMapAsync(db, facts.CompanyId, types);
        var usage = await LoadUsageAsync(db, employeeId, currentRequestId: 0, asOf.Year);

        var sourceIds = policies.Values
            .Where(policy => policy.RequiresBalance)
            .Select(policy => policy.BalanceSourceRequestTypeId ?? policy.RequestTypeId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        var result = new List<BalanceSnapshot>();
        foreach (var sourceId in sourceIds)
        {
            if (!policies.TryGetValue(sourceId, out var sourcePolicy))
                continue;

            var entitlement = CalculateAccruedEntitlement(sourcePolicy, facts.HireDate, asOf);
            entitlement += await CalculateCarryForwardAsync(
                db, employeeId, facts, sourcePolicy, sourceId, policies, types, asOf);

            var reserved = await SumUsageForSourceAsync(
                db, employeeId, facts.CompanyId, sourceId, usage, policies, types);

            result.Add(new BalanceSnapshot(
                sourceId,
                sourcePolicy.RequestTypeName,
                sourcePolicy.CategoryName,
                entitlement,
                reserved,
                entitlement - reserved,
                sourcePolicy.BalanceUnit));
        }

        return result
            .OrderBy(snapshot => snapshot.CategoryName)
            .ThenBy(snapshot => snapshot.RequestTypeName)
            .ToList();
    }

    private static List<RequestSlice> ExpandAcrossYears(IReadOnlyList<RequestSlice> requests)
    {
        var result = new List<RequestSlice>();
        foreach (var request in requests)
        {
            var start = request.FromDate;
            while (start.Year < request.ToDate.Year)
            {
                var end = new DateOnly(start.Year, 12, 31);
                result.Add(new RequestSlice(start, end, request.StartTime, request.EndTime));
                start = new DateOnly(start.Year + 1, 1, 1);
            }
            result.Add(new RequestSlice(start, request.ToDate, request.StartTime, request.EndTime));
        }
        return result;
    }

    private static async Task<EmployeeFacts> LoadEmployeeFactsAsync(ApplicationDbContext db,int employeeId)
    {
        var rows=await HrmsDatabase.QueryAsync(db,"SELECT TOP 1 ISNULL(CompanyId,0) CompanyId,HireDate FROM Employees WHERE Id=@Id AND IsDeleted=0;",
            c=>HrmsDatabase.AddParameter(c,"@Id",employeeId),r=>new EmployeeFacts(HrmsDatabase.GetInt(r,"CompanyId"),HrmsDatabase.GetDateOnly(r,"HireDate")));
        return rows.FirstOrDefault() ?? new(0,null);
    }

    private static async Task<Dictionary<int,Policy>> BuildPolicyMapAsync(ApplicationDbContext db,int companyId,IReadOnlyList<RequestTypeStore.ReqType> types)
    {
        var stored=await LoadStoredAsync(db,companyId); var byId=stored.ToDictionary(x=>x.RequestTypeId); var names=types.ToDictionary(x=>x.Id,x=>x.Name);
        return types.ToDictionary(type=>type.Id,type=>BuildEffective(companyId,type,byId,names));
    }

    private static Policy BuildEffective(int companyId,RequestTypeStore.ReqType type,IReadOnlyDictionary<int,StoredPolicy> stored,IReadOnlyDictionary<int,string> names)
    {
        if (stored.TryGetValue(type.Id,out var row))
        {
            var sourceId=row.BalanceSourceRequestTypeId ?? type.Id;
            return new Policy { CompanyId=companyId,RequestTypeId=type.Id,RequestTypeName=type.Name,CategoryName=type.CategoryName,NeedsTime=type.NeedsTime,
                RequiresBalance=row.RequiresBalance,EntitlementAmount=row.EntitlementAmount,BalanceUnit=NormalizeUnit(row.BalanceUnit),AllowNegative=row.AllowNegative,
                NegativeLimitAmount=row.NegativeLimitAmount,BalanceSourceRequestTypeId=row.BalanceSourceRequestTypeId,BalanceSourceName=names.GetValueOrDefault(sourceId,type.Name),
                HoursPerDayOverride=row.HoursPerDayOverride,MaxPerRequestAmount=row.MaxPerRequestAmount,MaxPerYearAmount=row.MaxPerYearAmount,MinimumNoticeDays=row.MinimumNoticeDays,
                EligibilityDays=row.EligibilityDays,CarryForwardEnabled=row.CarryForwardEnabled,CarryForwardMaxAmount=row.CarryForwardMaxAmount,CarryForwardExpiryMonths=row.CarryForwardExpiryMonths,
                AccrualMethod=NormalizeAccrual(row.AccrualMethod),ProrateOnHire=row.ProrateOnHire,AllowRetroactive=row.AllowRetroactive,ReasonRequired=row.ReasonRequired,
                AttachmentRequiredOverride=row.AttachmentRequiredOverride,IsConfigured=true };
        }
        return BuildDefault(companyId,type);
    }

    private static Policy BuildDefault(int companyId,RequestTypeStore.ReqType type)
    {
        var code=RequestTypeEffectCatalog.EffectiveCode(type);
        decimal? entitlement=type.HasBalance ? type.AllowedDays : null;
        if (entitlement is null) entitlement=code switch { RequestTypeEffectCatalog.LeaveAnnual=>21m,RequestTypeEffectCatalog.LeaveSick=>30m,_=>null };
        return new Policy { CompanyId=companyId,RequestTypeId=type.Id,RequestTypeName=type.Name,CategoryName=type.CategoryName,NeedsTime=type.NeedsTime,
            RequiresBalance=type.HasBalance||entitlement.HasValue,EntitlementAmount=entitlement,BalanceUnit="Days",AllowNegative=false,BalanceSourceName=type.Name,
            HoursPerDayOverride=0m,AccrualMethod="FullUpfront",AllowRetroactive=true,AttachmentRequiredOverride=null,IsConfigured=false };
    }

    private static Task<List<StoredPolicy>> LoadStoredAsync(ApplicationDbContext db,int companyId)=>HrmsDatabase.QueryAsync(db,"""
SELECT RequestTypeId,RequiresBalance,EntitlementAmount,AllowNegative,BalanceSourceRequestTypeId,HoursPerDayOverride,BalanceUnit,
 NegativeLimitAmount,MaxPerRequestAmount,MaxPerYearAmount,MinimumNoticeDays,EligibilityDays,CarryForwardEnabled,CarryForwardMaxAmount,
 CarryForwardExpiryMonths,AccrualMethod,ProrateOnHire,AllowRetroactive,ReasonRequired,AttachmentRequiredOverride
FROM CompanyLeavePolicies WHERE CompanyId=@CompanyId;
""",c=>HrmsDatabase.AddParameter(c,"@CompanyId",companyId),r=>new StoredPolicy(HrmsDatabase.GetInt(r,"RequestTypeId"),HrmsDatabase.GetBool(r,"RequiresBalance"),
    HrmsDatabase.GetNullableDecimal(r,"EntitlementAmount"),HrmsDatabase.GetBool(r,"AllowNegative"),HrmsDatabase.GetNullableInt(r,"BalanceSourceRequestTypeId"),
    HrmsDatabase.GetNullableDecimal(r,"HoursPerDayOverride")??0m,HrmsDatabase.GetString(r,"BalanceUnit"),HrmsDatabase.GetNullableDecimal(r,"NegativeLimitAmount"),
    HrmsDatabase.GetNullableDecimal(r,"MaxPerRequestAmount"),HrmsDatabase.GetNullableDecimal(r,"MaxPerYearAmount"),HrmsDatabase.GetInt(r,"MinimumNoticeDays"),
    HrmsDatabase.GetInt(r,"EligibilityDays"),HrmsDatabase.GetBool(r,"CarryForwardEnabled"),HrmsDatabase.GetNullableDecimal(r,"CarryForwardMaxAmount"),
    HrmsDatabase.GetNullableInt(r,"CarryForwardExpiryMonths"),HrmsDatabase.GetString(r,"AccrualMethod"),HrmsDatabase.GetBool(r,"ProrateOnHire"),
    HrmsDatabase.GetBool(r,"AllowRetroactive"),HrmsDatabase.GetBool(r,"ReasonRequired"),r["AttachmentRequiredOverride"]==DBNull.Value?null:HrmsDatabase.GetBool(r,"AttachmentRequiredOverride")));

    private static async Task<List<UsageRow>> LoadUsageAsync(ApplicationDbContext db,int employeeId,int currentRequestId,int year)
    {
        var from=new DateOnly(year,1,1); var to=new DateOnly(year,12,31);
        return await HrmsDatabase.QueryAsync(db,"""
SELECT Id,RequestTypeId,ISNULL(RequestType,N'') RequestType,COALESCE(FromDate,RequestDate,CAST(CreatedAt AS date)) FromDate,
 COALESCE(ToDate,FromDate,RequestDate,CAST(CreatedAt AS date)) ToDate,StartTime,EndTime
FROM SelfServiceRequests WHERE EmployeeId=@EmployeeId AND Id<>@CurrentRequestId AND Status IN(N'Pending',N'Approved')
 AND COALESCE(FromDate,RequestDate,CAST(CreatedAt AS date))<=@ToDate AND COALESCE(ToDate,FromDate,RequestDate,CAST(CreatedAt AS date))>=@FromDate;
""",c=>{HrmsDatabase.AddParameter(c,"@EmployeeId",employeeId);HrmsDatabase.AddParameter(c,"@CurrentRequestId",currentRequestId);HrmsDatabase.AddParameter(c,"@FromDate",from);HrmsDatabase.AddParameter(c,"@ToDate",to);},
            r=>new UsageRow(HrmsDatabase.GetInt(r,"Id"),HrmsDatabase.GetNullableInt(r,"RequestTypeId"),HrmsDatabase.GetString(r,"RequestType"),HrmsDatabase.GetDateOnly(r,"FromDate")??from,HrmsDatabase.GetDateOnly(r,"ToDate")??from,HrmsDatabase.GetTimeSpan(r,"StartTime"),HrmsDatabase.GetTimeSpan(r,"EndTime")));
    }

    private static async Task<decimal> SumUsageForSourceAsync(ApplicationDbContext db,int employeeId,int companyId,int sourceId,IReadOnlyList<UsageRow> usage,IReadOnlyDictionary<int,Policy> policies,IReadOnlyList<RequestTypeStore.ReqType> types)
    {
        var byId=types.ToDictionary(x=>x.Id); var byName=types.GroupBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
        var sourceUnit=policies.TryGetValue(sourceId,out var source)?source.BalanceUnit:"Days"; decimal total=0m;
        foreach(var row in usage)
        {
            RequestTypeStore.ReqType? rowType=null;
            if(row.RequestTypeId is >0 && byId.TryGetValue(row.RequestTypeId.Value,out var found)) rowType=found; else byName.TryGetValue(row.RequestType,out rowType);
            if(rowType is null||!policies.TryGetValue(rowType.Id,out var rowPolicy)||!rowPolicy.RequiresBalance) continue;
            if((rowPolicy.BalanceSourceRequestTypeId??rowPolicy.RequestTypeId)!=sourceId) continue;
            total+=await CalculateDebitAsync(db,employeeId,companyId,row.FromDate,row.ToDate,row.StartTime,row.EndTime,sourceUnit,rowPolicy.HoursPerDayOverride);
        }
        return Math.Round(total,4,MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateAccruedEntitlement(Policy policy,DateOnly? hireDate,DateOnly asOf)
    {
        var annual=policy.EntitlementAmount??0m; if(annual<=0m)return 0m;
        if(hireDate is { } futureHire && futureHire>asOf) return 0m;
        var yearStart=new DateOnly(asOf.Year,1,1); var start=yearStart;
        if(hireDate is { } hire && hire.Year==asOf.Year && hire>start) start=hire;
        decimal factor=NormalizeAccrual(policy.AccrualMethod) switch {
            "Monthly"=>Math.Clamp((asOf.Month-start.Month+1)/12m,0m,1m),
            "Daily"=>Math.Clamp((asOf.DayNumber-start.DayNumber+1)/(decimal)(new DateOnly(asOf.Year,12,31).DayNumber-yearStart.DayNumber+1),0m,1m),
            _=>policy.ProrateOnHire&&start>yearStart?(new DateOnly(asOf.Year,12,31).DayNumber-start.DayNumber+1)/(decimal)(new DateOnly(asOf.Year,12,31).DayNumber-yearStart.DayNumber+1):1m };
        return Math.Round(annual*factor,4,MidpointRounding.AwayFromZero);
    }

    private static async Task<decimal> CalculateCarryForwardAsync(ApplicationDbContext db,int employeeId,EmployeeFacts facts,Policy sourcePolicy,int sourceId,IReadOnlyDictionary<int,Policy> policies,IReadOnlyList<RequestTypeStore.ReqType> types,DateOnly asOf)
    {
        if(!sourcePolicy.CarryForwardEnabled||asOf.Year<=1)return 0m;
        if(sourcePolicy.CarryForwardExpiryMonths is { } months && months>=0 && asOf>new DateOnly(asOf.Year,1,1).AddMonths(months))return 0m;
        var py=asOf.Year-1; var priorEnd=new DateOnly(py,12,31); var priorEntitlement=CalculateAccruedEntitlement(sourcePolicy,facts.HireDate,priorEnd); if(priorEntitlement<=0m)return 0m;
        var priorRows=await LoadUsageAsync(db,employeeId,0,py); var priorUsed=await SumUsageForSourceAsync(db,employeeId,facts.CompanyId,sourceId,priorRows,policies,types);
        var carry=Math.Max(0m,priorEntitlement-priorUsed); if(sourcePolicy.CarryForwardMaxAmount is { } cap)carry=Math.Min(carry,cap); return carry;
    }

    private static async Task<decimal> CalculateDebitAsync(ApplicationDbContext db,int employeeId,int companyId,DateOnly fromDate,DateOnly toDate,TimeSpan? startTime,TimeSpan? endTime,string unit,decimal hoursOverride)
    {
        unit=NormalizeUnit(unit);
        if(startTime.HasValue&&endTime.HasValue)
        {
            var duration=endTime.Value>startTime.Value?endTime.Value-startTime.Value:TimeSpan.FromDays(1)-startTime.Value+endTime.Value;
            var hours=Math.Max(0m,(decimal)duration.TotalHours); if(unit=="Hours")return Math.Round(hours,4,MidpointRounding.AwayFromZero);
            var daily=await ResolveDailyHoursAsync(db,employeeId,companyId,fromDate,hoursOverride); return daily>0?Math.Round(hours/daily,4,MidpointRounding.AwayFromZero):0m;
        }
        if(unit=="Days")return toDate.DayNumber-fromDate.DayNumber+1;
        decimal total=0m; for(var date=fromDate;date<=toDate;date=date.AddDays(1))total+=await ResolveDailyHoursAsync(db,employeeId,companyId,date,hoursOverride); return Math.Round(total,4,MidpointRounding.AwayFromZero);
    }

    private static async Task<decimal> ResolveDailyHoursAsync(ApplicationDbContext db,int employeeId,int companyId,DateOnly date,decimal overrideHours)
    {
        if(overrideHours>0m)return overrideHours; var dayIndex=((int)date.DayOfWeek+1)%7;
        var shiftHours=await HrmsDatabase.ScalarAsync<decimal?>(db,"""
SELECT TOP 1 CASE WHEN st.IsFlexible=1 AND st.FlexDailyHours>0 THEN st.FlexDailyHours ELSE sd.WorkHours END
FROM EmployeeShiftTypes est JOIN ShiftTypes st ON st.Id=est.ShiftTypeId LEFT JOIN ShiftTypeDays sd ON sd.ShiftTypeId=st.Id AND sd.DayIndex=@DayIndex
WHERE est.EmployeeId=@EmployeeId AND st.IsActive=1;
""",c=>{HrmsDatabase.AddParameter(c,"@EmployeeId",employeeId);HrmsDatabase.AddParameter(c,"@DayIndex",dayIndex);});
        if(shiftHours is >0m)return shiftHours.Value; var companyPolicy=await AttendanceSalaryLinkSettings.LoadAsync(db,companyId);
        return companyPolicy.StandardDailyHours>0m?companyPolicy.StandardDailyHours:AttendanceSalaryLink.StandardDailyHours;
    }

    private static string NormalizeUnit(string? value)=>string.Equals(value,"Hours",StringComparison.OrdinalIgnoreCase)?"Hours":"Days";
    private static string NormalizeAccrual(string? value)=>value switch{"Monthly"=>"Monthly","Daily"=>"Daily",_=>"FullUpfront"};
    private static string UnitLabel(string? unit)=>NormalizeUnit(unit)=="Hours"?"ساعة":"يوم";
}