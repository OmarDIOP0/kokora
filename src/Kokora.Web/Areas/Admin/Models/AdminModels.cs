using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Kokora.Domain.Users;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Web.Areas.Admin.Models;

/// <summary>Libellés français des énumérations et listes d'options.</summary>
public static class Labels
{
    public static string Of(PhaseType t) => t == PhaseType.League ? "Championnat (poules)" : "Élimination directe";
    public static string Of(Legs l) => l == Legs.Single ? "Aller simple" : "Aller-retour";
    public static string Of(PlayerPosition p) => p switch
    {
        PlayerPosition.Goalkeeper => "Gardien",
        PlayerPosition.Defender => "Défenseur",
        PlayerPosition.Midfielder => "Milieu",
        PlayerPosition.Forward => "Attaquant",
        _ => "Non précisé"
    };
    public static string Short(PlayerPosition p) => p switch
    {
        PlayerPosition.Goalkeeper => "G", PlayerPosition.Defender => "D", PlayerPosition.Midfielder => "M",
        PlayerPosition.Forward => "A", _ => "–"
    };
    public static string Of(TieBreaker t) => t switch
    {
        TieBreaker.GoalDifference => "Différence de buts",
        TieBreaker.GoalsFor => "Buts marqués",
        TieBreaker.HeadToHead => "Confrontation directe",
        TieBreaker.FairPlay => "Fair-play (moins de cartons)",
        TieBreaker.Wins => "Nombre de victoires",
        _ => "Tirage au sort / décision"
    };

    public static string AuditAction(string action) => action switch
    {
        "Added" => "Création",
        "Modified" => "Modification",
        "Deleted" => "Suppression",
        _ => action
    };

    public static string Entity(string type) => type switch
    {
        "Season" => "Saison", "Competition" => "Compétition", "Phase" => "Phase", "Group" => "Poule",
        "Round" => "Tour", "Qualification" => "Qualification", "PointAdjustment" => "Pénalité de points",
        "Club" => "Équipe", "Player" => "Joueur", "SquadMember" => "Effectif", "Stadium" => "Stade", "Referee" => "Arbitre",
        "Match" => "Match", "MatchEvent" => "Événement de match", "Suspension" => "Suspension", "Protest" => "Réserve",
        "Article" => "Info", "ArticleCategory" => "Catégorie", "Photo" => "Photo", "Comment" => "Commentaire",
        _ => type
    };

    public static IEnumerable<SelectListItem> Enum<T>(Func<T, string> label) where T : struct, System.Enum =>
        System.Enum.GetValues<T>().Select(v => new SelectListItem(label(v), Convert.ToInt32(v).ToString()));

    public static readonly IReadOnlyList<SelectListItem> Zones =
        [new("Zone 5A", "5A"), new("Zone 5B", "5B")];
}

/// <summary>Listes déroulantes (équipes, stades, arbitres, phases) de la saison de travail.</summary>
public class Lookups(IAppDbContext db)
{
    public async Task<List<SelectListItem>> ClubsAsync(bool onlyActive = true, CancellationToken ct = default)
    {
        var q = db.Clubs.AsNoTracking();
        if (onlyActive) q = q.Where(c => c.IsActive);
        var clubs = await q.OrderBy(c => c.Zone).ThenBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Zone }).ToListAsync(ct);
        var groups = new Dictionary<string, SelectListGroup>();
        return clubs.Select(c =>
        {
            var key = c.Zone is null ? "Sans zone" : $"Zone {c.Zone}";
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new SelectListGroup { Name = key };
            return new SelectListItem(c.Name, c.Id.ToString()) { Group = g };
        }).ToList();
    }

    public async Task<List<SelectListItem>> StadiumsAsync(CancellationToken ct = default) =>
        (await db.Stadiums.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();

    public async Task<List<SelectListItem>> RefereesAsync(CancellationToken ct = default) =>
        (await db.Referees.AsNoTracking().OrderBy(s => s.FullName).ToListAsync(ct)).Select(s => new SelectListItem(s.FullName, s.Id.ToString())).ToList();

    public async Task<List<SelectListItem>> PhasesAsync(int seasonId, CancellationToken ct = default)
    {
        var phases = await db.Phases.AsNoTracking().Where(p => p.Competition.SeasonId == seasonId)
            .OrderBy(p => p.Competition.Order).ThenBy(p => p.Order)
            .Select(p => new { p.Id, p.Name, Competition = p.Competition.Name }).ToListAsync(ct);
        var groups = new Dictionary<string, SelectListGroup>();
        return phases.Select(p =>
        {
            if (!groups.TryGetValue(p.Competition, out var g)) groups[p.Competition] = g = new SelectListGroup { Name = p.Competition };
            return new SelectListItem(p.Name, p.Id.ToString()) { Group = g };
        }).ToList();
    }

    public async Task<List<SelectListItem>> CompetitionsAsync(int seasonId, CancellationToken ct = default) =>
        (await db.Competitions.AsNoTracking().Where(c => c.SeasonId == seasonId).OrderBy(c => c.Order).ToListAsync(ct))
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();

    /// <summary>Poules et tours de toutes les phases de la saison (pour le formulaire de match).</summary>
    public async Task<(List<PhaseOption> Phases, List<SelectListItem> Groups, List<SelectListItem> Rounds)> MatchContextAsync(int seasonId, CancellationToken ct = default)
    {
        var phases = await db.Phases.AsNoTracking().Where(p => p.Competition.SeasonId == seasonId)
            .OrderBy(p => p.Competition.Order).ThenBy(p => p.Order)
            .Select(p => new PhaseOption(p.Id, p.Competition.Name + " · " + p.Name, p.Type)).ToListAsync(ct);
        var groups = await db.Groups.AsNoTracking().Where(g => g.Phase.Competition.SeasonId == seasonId)
            .OrderBy(g => g.Order).Select(g => new { g.Id, g.Name, g.PhaseId }).ToListAsync(ct);
        var rounds = await db.Rounds.AsNoTracking().Where(r => r.Phase.Competition.SeasonId == seasonId)
            .OrderBy(r => r.Order).Select(r => new { r.Id, r.Name, r.PhaseId }).ToListAsync(ct);
        return (phases,
            groups.Select(g => new SelectListItem(g.Name, g.Id.ToString()) { Group = new SelectListGroup { Name = g.PhaseId.ToString() } }).ToList(),
            rounds.Select(r => new SelectListItem(r.Name, r.Id.ToString()) { Group = new SelectListGroup { Name = r.PhaseId.ToString() } }).ToList());
    }
}

public record PhaseOption(int Id, string Name, PhaseType Type);

// ---------------------------------------------------------------- Vues

public record SeasonsPageVm(IReadOnlyList<Season> Seasons, int? WorkingSeasonId);

public class CompetitionFormVm
{
    public CompetitionInput Input { get; set; } = new();
    public string SeasonName { get; set; } = "";
}

public class PhasePageVm
{
    public required Phase Phase { get; init; }
    public required List<SelectListItem> Clubs { get; init; }
    public GroupInput NewGroup { get; init; } = new();
    public KnockoutInput Knockout { get; init; } = new();
    public IReadOnlyList<AdminMatchItem> BracketMatches { get; init; } = [];
    public Dictionary<int, int> MatchCountByGroup { get; init; } = [];
    public IReadOnlyList<QualificationSlotVm> Slots { get; init; } = [];
    public IReadOnlyList<SourceOption> Sources { get; init; } = [];
}

public class ClubFormVm
{
    public ClubInput Input { get; set; } = new();
    public string? LogoPath { get; set; }
    public IFormFile? Logo { get; set; }
}

public class PlayerFormVm
{
    public PlayerInput Input { get; set; } = new();
    public string? PhotoPath { get; set; }
    public IFormFile? Photo { get; set; }
    public List<SelectListItem> Clubs { get; set; } = [];
    public string SeasonName { get; set; } = "";
}

public class PlayersPageVm
{
    public List<PlayerListItem> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public string? Search { get; init; }
    public int? ClubId { get; init; }
    public List<SelectListItem> Clubs { get; init; } = [];
    public bool HasMore => Page * PageSize < Total;
}

public class VenuesPageVm
{
    public List<Stadium> Stadiums { get; init; } = [];
    public List<Referee> Referees { get; init; } = [];
    public StadiumInput NewStadium { get; init; } = new();
    public RefereeInput NewReferee { get; init; } = new();
}

public class MatchesPageVm
{
    public List<AdminMatchItem> Items { get; init; } = [];
    public int? CompetitionId { get; init; }
    public int? ClubId { get; init; }
    public DateOnly? Day { get; init; }
    public bool Missing { get; init; }
    public List<SelectListItem> Competitions { get; init; } = [];
    public List<SelectListItem> Clubs { get; init; } = [];
}

public class MatchFormVm
{
    public MatchInput Input { get; set; } = new();
    public List<PhaseOption> Phases { get; set; } = [];
    public List<SelectListItem> Groups { get; set; } = [];
    public List<SelectListItem> Rounds { get; set; } = [];
    public List<SelectListItem> Clubs { get; set; } = [];
    public List<SelectListItem> Stadiums { get; set; } = [];
    public List<SelectListItem> Referees { get; set; } = [];
    public MatchStatus Status { get; set; }
}

public class GenerateFixturesVm
{
    public FixtureGenerationInput Input { get; set; } = new();
    public required Group Group { get; set; }
    public List<SelectListItem> Stadiums { get; set; } = [];
    public List<ProposedFixture>? Preview { get; set; }
    public int ExistingMatches { get; set; }
}

public class AuditPageVm
{
    public List<AuditLog> Items { get; init; } = [];
    public int Total { get; init; }
    public AuditFilter Filter { get; init; } = new();
    public List<string> EntityTypes { get; init; } = [];
}

public class ImportPageVm
{
    public ImportReport? Report { get; init; }
    public string SeasonName { get; init; } = "";
}

// ---------------------------------------------------------------- Infos

public class ArticlesPageVm
{
    public List<ArticleListItem> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public string? Status { get; init; }
    public string? Search { get; init; }
    public ArticleCounts Counts { get; init; } = new(0, 0, 0, 0, 0);
    public bool HasMore => Page * ArticleAdminService.PageSize < Total;
}

public class ArticleFormVm
{
    public ArticleInput Input { get; set; } = new();
    public IFormFile? Cover { get; set; }
    public Kokora.Domain.Content.Article? Existing { get; set; }
    public List<SelectListItem> Categories { get; set; } = [];
    public List<SelectListItem> Tags { get; set; } = [];
    public List<SelectListItem> Clubs { get; set; } = [];
    public List<SelectListItem> Matches { get; set; } = [];
    public List<PhotoItem> Photos { get; set; } = [];
}

public class CategoriesPageVm
{
    public List<CategoryItem> Items { get; init; } = [];
    public CategoryInput NewCategory { get; init; } = new();
}

/// <summary>Galerie éditable (partagée entre infos et matchs).</summary>
public record GalleryVm(PhotoOwner Owner, List<PhotoItem> Photos)
{
    public string OwnerQuery => Owner.ArticleId is { } a ? $"info={a}" : $"match={Owner.MatchId}";
}

public class MatchPhotosVm
{
    public int MatchId { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public DateTimeOffset? KickoffAt { get; init; }
    public required GalleryVm Gallery { get; init; }
}

public record MatchPhotoItem(int Id, string Title, string Competition, DateTimeOffset? KickoffAt, int Photos);

public static class ArticleLabels
{
    public static string Of(ArticleStatus s) => s switch
    {
        ArticleStatus.Draft => "Brouillon", ArticleStatus.Scheduled => "Programmée",
        ArticleStatus.Published => "Publiée", _ => "Archivée"
    };

    public static string Badge(ArticleStatus s) => s switch
    {
        ArticleStatus.Published => "win", ArticleStatus.Scheduled => "brand", ArticleStatus.Draft => "warn", _ => ""
    };
}

// ---------------------------------------------------------------- Communauté

public record CommentsPageVm(CommentStatus Status, List<Kokora.Application.Engagement.ModerationItem> Items, int Pending);

public record UserItem(string Id, string DisplayName, string Login, string Role, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, bool Locked);

public record UsersPageVm(List<UserItem> Items, int Total, string? Search, string? Role, List<SelectListItem> Roles);

public record NotificationsPageVm(Kokora.Application.Engagement.PushAudience Audience, bool Enabled, List<SelectListItem> Clubs);
