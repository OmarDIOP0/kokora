using System.ComponentModel.DataAnnotations;
using Kokora.Domain.Enums;

namespace Kokora.Application.Admin;

// Modèles de saisie des formulaires d'administration. Messages en français.

public class SeasonInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "L'année est obligatoire.")]
    [Range(2000, 2100, ErrorMessage = "Année invalide.")]
    [Display(Name = "Année")]
    public int Year { get; set; } = DateTime.UtcNow.Year;

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(80, ErrorMessage = "80 caractères maximum.")]
    [Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Display(Name = "Saison en cours")]
    public bool IsCurrent { get; set; }
}

public class CompetitionInput
{
    public int Id { get; set; }
    public int SeasonId { get; set; }

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(120, ErrorMessage = "120 caractères maximum.")]
    [Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Le nom court est obligatoire.")]
    [StringLength(40, ErrorMessage = "40 caractères maximum.")]
    [Display(Name = "Nom court")]
    public string ShortName { get; set; } = "";

    [Required(ErrorMessage = "La couleur est obligatoire.")]
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur au format #RRGGBB.")]
    [Display(Name = "Couleur")]
    public string Color { get; set; } = "#0E6B3A";

    [StringLength(1000, ErrorMessage = "1000 caractères maximum.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Range(20, 60, ErrorMessage = "Entre 20 et 60 minutes.")]
    [Display(Name = "Durée d'une mi-temps (min)")]
    public int HalfDurationMinutes { get; set; } = 45;

    [Range(5, 30, ErrorMessage = "Entre 5 et 30 minutes.")]
    [Display(Name = "Durée d'une mi-temps de prolongation (min)")]
    public int ExtraTimeHalfDurationMinutes { get; set; } = 15;

    [Display(Name = "Visible sur le site")]
    public bool IsPublished { get; set; } = true;

    // --- Points ---
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Victoire")]
    public int PointsForWin { get; set; } = 3;
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Nul")]
    public int PointsForDraw { get; set; } = 1;
    [Range(-5, 10, ErrorMessage = "Entre -5 et 10.")] [Display(Name = "Défaite")]
    public int PointsForLoss { get; set; }
    [Range(-10, 10, ErrorMessage = "Entre -10 et 10.")] [Display(Name = "Défaite par forfait")]
    public int PointsForForfeitLoss { get; set; }
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Buts attribués (forfait)")]
    public int ForfeitGoalsFor { get; set; } = 3;
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Buts encaissés (forfait)")]
    public int ForfeitGoalsAgainst { get; set; }

    /// <summary>Critères de départage, dans l'ordre (réordonnés par glisser-déposer).</summary>
    public List<TieBreaker> TieBreakers { get; set; } =
        [TieBreaker.GoalDifference, TieBreaker.GoalsFor, TieBreaker.HeadToHead, TieBreaker.FairPlay];

    // --- Suspensions ---
    [Display(Name = "Suspensions automatiques")]
    public bool SuspensionsEnabled { get; set; } = true;
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Cartons jaunes avant suspension")]
    public int YellowCardsThreshold { get; set; } = 3;
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Matchs (cumul de jaunes)")]
    public int MatchesForYellowAccumulation { get; set; } = 1;
    [Range(0, 10, ErrorMessage = "Entre 0 et 10.")] [Display(Name = "Matchs (2e jaune)")]
    public int MatchesForSecondYellow { get; set; } = 1;
    [Range(0, 20, ErrorMessage = "Entre 0 et 20.")] [Display(Name = "Matchs (rouge direct)")]
    public int MatchesForDirectRed { get; set; } = 1;
    [Display(Name = "Remettre les jaunes à zéro à chaque phase")]
    public bool ResetYellowsEachPhase { get; set; }
}

public class PhaseInput
{
    public int Id { get; set; }
    public int CompetitionId { get; set; }

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(120, ErrorMessage = "120 caractères maximum.")]
    [Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Display(Name = "Type")]
    public PhaseType Type { get; set; } = PhaseType.League;

    [Display(Name = "Matchs")]
    public Legs Legs { get; set; } = Legs.Single;

    [Display(Name = "Prolongations en cas d'égalité")]
    public bool HasExtraTime { get; set; }

    [Display(Name = "Tirs au but en cas d'égalité")]
    public bool HasPenalties { get; set; } = true;
}

public class GroupInput
{
    public int Id { get; set; }
    public int PhaseId { get; set; }

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(60, ErrorMessage = "60 caractères maximum.")]
    [Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Display(Name = "Équipes")]
    public List<int> ClubIds { get; set; } = [];

    [Range(0, 32, ErrorMessage = "Entre 0 et 32.")]
    [Display(Name = "Places qualificatives")]
    public int QualifiedCount { get; set; } = 2;

    [StringLength(60)]
    [Display(Name = "Libellé des places qualificatives")]
    public string? QualifiedLabel { get; set; } = "Qualifié";

    [Range(0, 32, ErrorMessage = "Entre 0 et 32.")]
    [Display(Name = "Places de barrage")]
    public int PlayoffCount { get; set; }

    [Range(0, 32, ErrorMessage = "Entre 0 et 32.")]
    [Display(Name = "Places éliminatoires (fin de tableau)")]
    public int EliminatedCount { get; set; }
}

public class KnockoutInput
{
    public int PhaseId { get; set; }

    [Display(Name = "Nombre d'équipes")]
    public int TeamCount { get; set; } = 8;

    [Display(Name = "Match pour la 3e place")]
    public bool ThirdPlace { get; set; }
}

public class ClubInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(120, ErrorMessage = "120 caractères maximum.")]
    [Display(Name = "Nom complet")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Le nom court est obligatoire.")]
    [StringLength(30, ErrorMessage = "30 caractères maximum.")]
    [Display(Name = "Nom court")]
    public string ShortName { get; set; } = "";

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur au format #RRGGBB.")]
    [Display(Name = "Couleur principale")]
    public string PrimaryColor { get; set; } = "#0E6B3A";

    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur au format #RRGGBB.")]
    [Display(Name = "Couleur secondaire")]
    public string SecondaryColor { get; set; } = "#FFFFFF";

    [StringLength(120)] [Display(Name = "Quartier")]
    public string? Neighborhood { get; set; }

    [StringLength(20)] [Display(Name = "Zone")]
    public string? Zone { get; set; }

    [StringLength(120)] [Display(Name = "Responsable")]
    public string? ManagerName { get; set; }

    [StringLength(30)]
    [RegularExpression(@"^[0-9 +().-]{6,30}$", ErrorMessage = "Numéro de téléphone invalide.")]
    [Display(Name = "Contact")]
    public string? ContactPhone { get; set; }

    [Range(1900, 2100, ErrorMessage = "Année invalide.")]
    [Display(Name = "Année de création")]
    public int? FoundedYear { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Supprimer le logo")]
    public bool RemoveLogo { get; set; }
}

public class PlayerInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le prénom est obligatoire.")]
    [StringLength(80)] [Display(Name = "Prénom")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(80)] [Display(Name = "Nom")]
    public string LastName { get; set; } = "";

    [StringLength(60)] [Display(Name = "Surnom")]
    public string? Nickname { get; set; }

    [Display(Name = "Poste")]
    public PlayerPosition Position { get; set; }

    [Display(Name = "Date de naissance")]
    public DateOnly? BirthDate { get; set; }

    /// <summary>ASC pour la saison en cours (null = sans club).</summary>
    [Display(Name = "Équipe (saison en cours)")]
    public int? ClubId { get; set; }

    [Range(1, 99, ErrorMessage = "Numéro entre 1 et 99.")]
    [Display(Name = "Numéro")]
    public int? ShirtNumber { get; set; }

    [StringLength(40)] [Display(Name = "N° de licence")]
    public string? LicenseNumber { get; set; }

    [Display(Name = "Capitaine")]
    public bool IsCaptain { get; set; }

    [Display(Name = "Supprimer la photo")]
    public bool RemovePhoto { get; set; }
}

public class StadiumInput
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(120)] [Display(Name = "Nom")]
    public string Name { get; set; } = "";
    [StringLength(120)] [Display(Name = "Quartier")]
    public string? Neighborhood { get; set; }
    [Range(0, 100000, ErrorMessage = "Capacité invalide.")] [Display(Name = "Capacité")]
    public int? Capacity { get; set; }
}

public class RefereeInput
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Le nom est obligatoire.")]
    [StringLength(120)] [Display(Name = "Nom complet")]
    public string FullName { get; set; } = "";
    [StringLength(30)]
    [RegularExpression(@"^[0-9 +().-]{6,30}$", ErrorMessage = "Numéro de téléphone invalide.")]
    [Display(Name = "Téléphone")]
    public string? Phone { get; set; }
}

public class MatchInput
{
    public int Id { get; set; }

    [Required(ErrorMessage = "La phase est obligatoire.")]
    [Display(Name = "Phase")]
    public int PhaseId { get; set; }

    [Display(Name = "Poule")]
    public int? GroupId { get; set; }

    [Display(Name = "Tour")]
    public int? RoundId { get; set; }

    [Range(1, 99, ErrorMessage = "Journée entre 1 et 99.")]
    [Display(Name = "Journée")]
    public int? Matchday { get; set; }

    [Display(Name = "Équipe à domicile")]
    public int? HomeClubId { get; set; }

    [Display(Name = "Équipe à l'extérieur")]
    public int? AwayClubId { get; set; }

    [StringLength(80)] [Display(Name = "Libellé domicile (si inconnue)")]
    public string? HomePlaceholder { get; set; }

    [StringLength(80)] [Display(Name = "Libellé extérieur (si inconnue)")]
    public string? AwayPlaceholder { get; set; }

    /// <summary>Date et heure locales (Dakar).</summary>
    [Display(Name = "Date et heure")]
    public DateTime? KickoffLocal { get; set; }

    [Display(Name = "Stade")]
    public int? StadiumId { get; set; }

    [Display(Name = "Arbitre")]
    public int? RefereeId { get; set; }

    [Display(Name = "Match à l'affiche")]
    public bool IsFeatured { get; set; }

    [StringLength(1000)] [Display(Name = "Notes")]
    public string? Notes { get; set; }
}

public class FixtureGenerationInput
{
    public int GroupId { get; set; }

    [Display(Name = "Aller-retour")]
    public bool HomeAndAway { get; set; }

    [Required(ErrorMessage = "La date de la 1re journée est obligatoire.")]
    [Display(Name = "Date de la 1re journée")]
    public DateOnly? FirstDate { get; set; }

    [Range(1, 30, ErrorMessage = "Entre 1 et 30 jours.")]
    [Display(Name = "Jours entre deux journées")]
    public int DaysBetweenMatchdays { get; set; } = 7;

    [Display(Name = "Heure du premier match")]
    public TimeOnly FirstKickoff { get; set; } = new(16, 30);

    [Range(0, 300, ErrorMessage = "Entre 0 et 300 minutes.")]
    [Display(Name = "Minutes entre deux matchs le même jour")]
    public int MinutesBetweenMatches { get; set; }

    [Display(Name = "Stade par défaut")]
    public int? StadiumId { get; set; }

    [Display(Name = "Supprimer d'abord les matchs non joués de la poule")]
    public bool ReplaceUnplayed { get; set; }
}
