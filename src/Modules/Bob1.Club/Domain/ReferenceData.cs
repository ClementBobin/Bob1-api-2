namespace Bob1.Club.Domain;

// Plain reference data: no business rules, so no ceremony.
internal sealed class Division
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";   // "U13 M"
    public int SortOrder { get; set; }
    public string Category { get; set; } = ""; // U11, U13, U15, U17, U18, Senior
    public string Gender { get; set; } = "";   // Masculin, Féminin, Mixte
    public bool IsActive { get; set; } = true;
}

internal sealed class Team
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid DivisionId { get; set; }
    public string? CoachName { get; set; }
    public Guid? CoachUserId { get; set; }  // Foreign key to User if coach is a staff member
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? TrainingVenueId { get; set; }  // Foreign key to Venue
}

internal sealed class Venue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
}
