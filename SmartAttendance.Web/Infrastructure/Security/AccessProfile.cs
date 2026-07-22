namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// A user's resolved access across the Access Roles system, composed from all
/// their active roles. Pure and deterministic (see <see cref="Build"/>), so the
/// resolution rules are unit-testable without a database.
///
/// Safety model (fully additive): a user with no role of a given type is
/// unrestricted for it — page access is allowed and the data scope is "All".
/// Only an explicitly assigned role narrows access. Admin bypass is applied by
/// callers, above this profile.
/// </summary>
public sealed class AccessProfile
{
    /// <summary>True when the user has at least one active Pages role.</summary>
    public bool HasPagesRole { get; init; }

    /// <summary>Page code → union of granted actions across the user's Pages roles.</summary>
    public IReadOnlyDictionary<string, HashSet<string>> PageActions { get; init; } =
        new Dictionary<string, HashSet<string>>();

    /// <summary>Entity code → widest data scope across the user's Data roles.</summary>
    public IReadOnlyDictionary<string, string> DataScopes { get; init; } =
        new Dictionary<string, string>();

    // Widest first; a lower rank is broader. Used to merge multiple Data roles.
    private static readonly IReadOnlyList<string> ScopeOrder = new[]
    {
        "All", "OwnCompany", "OwnBranch", "OwnDepartment", "Self",
    };

    /// <summary>
    /// Whether the user may view a page. Unrestricted (no Pages role) → always
    /// true; otherwise the page must be granted the View action.
    /// </summary>
    public bool CanViewPage(string pageCode)
    {
        if (!HasPagesRole)
        {
            return true;
        }

        return PageActions.TryGetValue(pageCode, out var actions) && actions.Contains("View");
    }

    /// <summary>Whether the user may perform an action on a page (View/Create/Edit/Delete).</summary>
    public bool Can(string pageCode, string action)
    {
        if (!HasPagesRole)
        {
            return true;
        }

        return PageActions.TryGetValue(pageCode, out var actions) && actions.Contains(action);
    }

    /// <summary>The effective data scope for an entity ("All" when the user has no Data role for it).</summary>
    public string DataScopeFor(string entityCode) =>
        DataScopes.TryGetValue(entityCode, out var scope) ? scope : "All";

    public static AccessProfile Build(
        bool hasPagesRole,
        IEnumerable<(string Key, IEnumerable<string> Actions)> pageGrants,
        IEnumerable<(string Key, string Scope)> dataGrants)
    {
        var pages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, actions) in pageGrants)
        {
            if (!pages.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pages[key] = set;
            }

            foreach (var action in actions)
            {
                set.Add(action);
            }
        }

        var scopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, scope) in dataGrants)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                continue;
            }

            if (!scopes.TryGetValue(key, out var current) || IsWider(scope, current))
            {
                scopes[key] = scope;
            }
        }

        return new AccessProfile
        {
            HasPagesRole = hasPagesRole,
            PageActions = pages,
            DataScopes = scopes,
        };
    }

    private static bool IsWider(string candidate, string current)
    {
        var candidateRank = ScopeOrder.ToList().IndexOf(candidate);
        var currentRank = ScopeOrder.ToList().IndexOf(current);
        if (candidateRank < 0)
        {
            return false;
        }

        if (currentRank < 0)
        {
            return true;
        }

        return candidateRank < currentRank;
    }
}
