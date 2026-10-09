using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Services;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Pages.Engagement;

namespace SmartAttendance.Tests;

/// <summary>Opt-in, new test-only LocalDB instance/catalog. Never accepts a production connection string.</summary>
public sealed class AnnouncementStudioSqlTests : IAsyncLifetime
{
    private ApplicationDbContext db = null!;
    private int companyA, companyB;
    private bool enabled;
    public async Task InitializeAsync()
    {
        enabled=Environment.GetEnvironmentVariable("ZYNORA_ANNOUNCEMENT_SQL_TESTS")=="1";
        if(!enabled)return;
        var catalog="CodexAnnouncementTemplateTest_"+Guid.NewGuid().ToString("N");
        db=new TestDb(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"Server=(localdb)\\CodexAnnouncementTests20261009;Database={catalog};Integrated Security=True;TrustServerCertificate=True").Options);
        await db.Database.EnsureCreatedAsync(); // Dedicated, synthetic test catalog only.
        var tenant=new Tenant{Code="0999",Name="Synthetic test tenant"};
        var a=new Company{Code="TEST-A",Name="Synthetic Company A",Tenant=tenant};
        var b=new Company{Code="TEST-B",Name="Synthetic Company B",Tenant=tenant};
        db.Companies.AddRange(a,b);await db.SaveChangesAsync();companyA=a.Id;companyB=b.Id;
    }
    public async Task DisposeAsync(){if(db!=null)await db.DisposeAsync();}
    [SkippableFact] public async Task ScopedLibraryRejectsAnotherCompanyDesignAndTemplate()
    {
        Skip.IfNot(enabled,"Set ZYNORA_ANNOUNCEMENT_SQL_TESTS=1 with the dedicated LocalDB instance.");
        var image=new AnnouncementStudioDesign{CompanyId=companyB,Name="Synthetic B",Data=[1,2,3]};db.AnnouncementStudioDesigns.Add(image);await db.SaveChangesAsync();
        var page=Page(companyA);
        Assert.IsType<NotFoundResult>(await page.OnGetImageAsync(image.Id));
        page.CompanyId=companyB;Assert.IsType<ForbidResult>(await page.OnGetImageAsync(image.Id));
        Assert.IsType<ForbidResult>(await page.OnPostSaveTemplateAsync());
        Assert.Empty(await db.AnnouncementStudioProfiles.ToListAsync());
    }
    [SkippableFact] public async Task TemplateSaveChecksDesignOwnershipAndConcurrency()
    {
        Skip.IfNot(enabled,"Dedicated LocalDB tests disabled.");
        var image=new AnnouncementStudioDesign{CompanyId=companyB,Name="Synthetic B",Data=[1,2,3]};db.AnnouncementStudioDesigns.Add(image);await db.SaveChangesAsync();
        var t=AnnouncementStudio.Defaults().First();t.DesignIds=[image.Id];var page=Page(companyA);page.TemplateJson=JsonSerializer.Serialize(t);
        Assert.IsType<PageResult>(await page.OnPostSaveTemplateAsync());Assert.Empty(await db.AnnouncementStudioProfiles.ToListAsync());
        t.DesignIds=[];page.TemplateJson=JsonSerializer.Serialize(t);Assert.IsType<RedirectToPageResult>(await page.OnPostSaveTemplateAsync());
        var before=await db.AnnouncementStudioProfiles.AsNoTracking().SingleAsync();
        page=Page(companyA);t.Name="Changed";page.TemplateJson=JsonSerializer.Serialize(t);page.Revision=Guid.NewGuid();
        Assert.IsType<PageResult>(await page.OnPostSaveTemplateAsync());
        Assert.Equal(before.DefinitionJson,(await db.AnnouncementStudioProfiles.AsNoTracking().SingleAsync()).DefinitionJson);
    }
    [SkippableFact] public async Task MultilingualDraftSnapshotsAndRetriesArePersistedOnce()
    {
        Skip.IfNot(enabled,"Dedicated LocalDB tests disabled.");
        var service=new AnnouncementService(db);var requestId=Guid.NewGuid();
        var request=new AnnouncementCreateRequest{RequestId=requestId,Title="Synthetic title",Body="Synthetic body",LanguageCode="ar",CompanyIds=[companyA],Translations=[new("en","Synthetic English","Synthetic body"),new("ckb-IQ","Synthetic Kurdish","Synthetic body")],PresentationJson=JsonSerializer.Serialize(new StudioPresentation(Guid.Empty,"cover","top","below",AnnouncementStudio.DefaultAsset("welcome")))};
        var actor=new AnnouncementActorContext{UserName="synthetic-admin",Role="Admin"};
        var first=await service.CreateAsync(request,actor);Assert.True(first.Success,first.Message);
        var second=await service.CreateAsync(request,actor);Assert.True(second.Success,second.Message);Assert.Equal(first.AnnouncementId,second.AnnouncementId);
        Assert.Single(await db.AnnouncementGroups.Where(g=>g.TranslationGroupId==requestId).ToListAsync());
        var contents=await db.AnnouncementContents.Where(c=>c.AnnouncementGroupId==first.AnnouncementId).ToListAsync();Assert.Equal(3,contents.Count);Assert.All(contents,c=>Assert.Equal(request.PresentationJson,c.PresentationJson));
    }
    [SkippableFact] public async Task ControlledSchemaExportIsRepeatableAndDoesNotTouchEmployeeData()
    {
        Skip.IfNot(enabled,"Dedicated LocalDB tests disabled.");
        await db.Database.ExecuteSqlRawAsync(AnnouncementStudioSchema.Sql);
        await db.Database.ExecuteSqlRawAsync(AnnouncementStudioSchema.Sql);
        Assert.Equal(2,await db.Companies.CountAsync());Assert.Empty(await db.Employees.ToListAsync());
        Assert.Equal(35,db.Model.FindEntityType(typeof(AnnouncementContent))!.FindProperty(nameof(AnnouncementContent.LanguageCode))!.GetMaxLength());
    }
    [SkippableFact] public async Task EmployeeDesignRequiresRecipientTenantAndSameCompany()
    {
        Skip.IfNot(enabled,"Dedicated LocalDB tests disabled.");
        var branch=new Branch{CompanyId=companyA,Code="SYN-B",Name="Synthetic branch"};
        var department=new Department{CompanyId=companyA,Code="SYN-D",Name="Synthetic department",Branch=branch};
        var employee=new Employee{EmployeeNo="SYN-1",FullName="Synthetic employee",CompanyId=companyA,Branch=branch,Department=department};
        db.Employees.Add(employee);await db.SaveChangesAsync();
        var design=new AnnouncementStudioDesign{CompanyId=companyA,Name="Synthetic artwork",Data=[1,2,3],IsActive=false};
        var foreign=new AnnouncementStudioDesign{CompanyId=companyB,Name="Synthetic other artwork",Data=[4,5,6]};
        db.AnnouncementStudioDesigns.AddRange(design,foreign);await db.SaveChangesAsync();
        var group=new AnnouncementGroup{Status=AnnouncementStatus.Published};
        var content=new AnnouncementContent{LanguageCode="ar",Title="Synthetic",Body="Synthetic",PresentationJson=JsonSerializer.Serialize(new StudioPresentation(design.Id,"contain","center","above"))};
        group.Contents.Add(content);group.Channels.Add(new(){ChannelType=AnnouncementChannelType.EmployeeWall,IsEnabled=true});
        group.Recipients.Add(new(){EmployeeId=employee.Id,ResolvedAtUtc=DateTime.UtcNow});db.AnnouncementGroups.Add(group);await db.SaveChangesAsync();
        var tenantId=(await db.Companies.FindAsync(companyA))!.TenantId;
        SmartAttendance.Web.Pages.EmployeePortal.AnnouncementDesignModel Endpoint(int employeeId,int? tenant)
        {
            var context=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("EmployeeId",employeeId.ToString()),new Claim("TenantId",tenant.ToString()??"")],"test"))};
            return new(db){PageContext=new PageContext{HttpContext=context}};
        }
        Assert.IsType<FileContentResult>(await Endpoint(employee.Id,tenantId).OnGetAsync(group.Id,design.Id));
        Assert.IsType<NotFoundResult>(await Endpoint(employee.Id,tenantId+1).OnGetAsync(group.Id,design.Id));
        Assert.IsType<NotFoundResult>(await Endpoint(employee.Id+1,tenantId).OnGetAsync(group.Id,design.Id));
        content.PresentationJson=JsonSerializer.Serialize(new StudioPresentation(foreign.Id,"contain","center","above"));await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Endpoint(employee.Id,tenantId).OnGetAsync(group.Id,foreign.Id));
        group.Status=AnnouncementStatus.Pending;await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Endpoint(employee.Id,tenantId).OnGetAsync(group.Id,foreign.Id));
    }
    private StudioModel Page(int company)
    {
        var context=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"synthetic-admin"),new Claim(ClaimTypes.Role,"Admin")],"test"))};
        context.RequestServices=new ServiceCollection().AddSingleton<ICompanyScopeProvider>(new Scope(companyA)).BuildServiceProvider();
        return new StudioModel(db,new AnnouncementService(db)){CompanyId=company,PageContext=new PageContext{HttpContext=context},TempData=new TempDataDictionary(context,new MemoryTempData())};
    }
    private sealed class TestDb(DbContextOptions<ApplicationDbContext> options):ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // Runtime-owned tables are excluded from production EF migrations, but required in this new synthetic catalog.
            foreach(var entity in builder.Model.GetEntityTypes()) entity.SetIsTableExcludedFromMigrations(false);
        }
    }
    private sealed class MemoryTempData:ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values){}
    }
    private sealed class Scope(int company):ICompanyScopeProvider
    {
        public Task<CompanyScope> GetAsync(CancellationToken cancellationToken=default)=>Task.FromResult(CompanyScope.ForCompanies([company]));
    }
}
