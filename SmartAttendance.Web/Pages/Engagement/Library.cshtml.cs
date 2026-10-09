using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Engagement;

// Separate page/view, sharing the existing scoped template/design handlers.
public class LibraryModel(ApplicationDbContext db, IAnnouncementService service, ILocalizationDictionaryService dictionary)
    : StudioModel(db, service, dictionary)
{
    public override async Task<IActionResult> OnGetAsync()
    {
        if (!CanEditLibrary) return Forbid();
        return await base.OnGetAsync();
    }
}
