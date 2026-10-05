using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformFormValidation
{
    // Independent forms share a PageModel, but must not share validation errors.
    // Retain all errors for the submitted form, including binding/conversion errors.
    public static void RemoveOtherForms(ModelStateDictionary modelState, params string[] otherForms)
    {
        foreach (var key in modelState.Keys.ToArray())
        {
            if (otherForms.Any(prefix =>
                key.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith(prefix + "[", StringComparison.OrdinalIgnoreCase)))
            {
                modelState.Remove(key);
            }
        }
    }
}
