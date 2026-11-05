using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bob1.Modules.Club.Api;

internal sealed record LookupOption(string Value, string Label);
internal sealed record SlotTemplateDto(string Key, string Role, string Kind);

internal sealed record LookupsDto(
    string[] Roles,
    string[] DivisionGenders,
    string[] DivisionCategories,
    string[] MatchStatuses,
    string[] SlotKinds,
    SlotTemplateDto[] SlotTemplates,
    LookupOption[] PointCategories,
    LookupOption[] NotificationTypes);

/// <summary>
/// Closed value lists the back-office needs for its dropdowns, so the front never hardcodes them.
/// Values mirror the enums / conventions documented on the entities (roles, SlotKind, MatchStatus, point categories).
/// </summary>
[Route(ApiRoutes.V1 + "/lookups")]
[Authorize]
[Tags("Club")]
internal sealed class LookupsController : ApiControllerBase
{
    private static readonly LookupsDto Value = new(
        Roles: ["Parent", "Staff", "Admin"],
        DivisionGenders: ["Masculin", "Féminin", "Mixte"],
        DivisionCategories: ["U11", "U13", "U15", "U17", "U18", "Senior"],
        MatchStatuses: ["Scheduled", "Cancelled", "Played"],
        SlotKinds: ["Volunteer", "Staff"],
        SlotTemplates:
        [
            new("arbitres", "Arbitres", "Staff"),
            new("chrono", "Chrono", "Staff"),
            new("mar", "MAR", "Staff"),
            new("buvette", "Buvette", "Volunteer")
        ],
        PointCategories:
        [
            new("presence", "Présence"),
            new("volunteer", "Bénévolat"),
            new("staff", "Officiel / staff"),
            new("carpool", "Covoiturage"),
            new("penalty", "Pénalité")
        ],
        NotificationTypes:
        [
            new("Général", "Général"),
            new("Match", "Match"),
            new("Urgent", "Urgent"),
            new("Pénalité", "Pénalité")
        ]);

    [HttpGet]
    public IActionResult Get() => Ok(Value);
}
