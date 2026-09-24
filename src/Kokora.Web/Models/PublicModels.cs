using Kokora.Application.Engagement;
using Kokora.Application.Public;
using Kokora.Web.Infrastructure;

namespace Kokora.Web.Models;

public enum MatchesView { Day, Upcoming, Results }

public class HomeVm
{
    public required PublicContext Ctx { get; init; }
    public MatchesView View { get; init; }
    public DateOnly Date { get; init; }
    public IReadOnlyList<DateOnly> Strip { get; init; } = [];
    public IReadOnlyDictionary<DateOnly, DayInfo> Days { get; init; } = new Dictionary<DateOnly, DayInfo>();
    public IReadOnlyList<MatchRowVm> Live { get; init; } = [];
    public IReadOnlyList<MatchGroupVm> Groups { get; init; } = [];
    public MatchListVm? List { get; init; }
    public MatchRowVm? Featured { get; init; }
    public StandingsTableVm? MiniStandings { get; init; }
    public string? MiniStandingsUrl { get; init; }
    public IReadOnlyList<TeamVm> FavoriteTeams { get; init; } = [];
    /// <summary>Jour avec des matchs le plus proche, quand la journée choisie est vide.</summary>
    public DateOnly? NearestDay { get; init; }

    public string Url(MatchesView? view = null, DateOnly? date = null, string? comp = null)
    {
        var v = view ?? View;
        var parts = new List<string>();
        if (v != MatchesView.Day) parts.Add("vue=" + (v == MatchesView.Upcoming ? "a-venir" : "resultats"));
        var d = date ?? (v == MatchesView.Day ? Date : null);
        if (v == MatchesView.Day && d is { } day && day != Kokora.Application.Common.KokoraTime.Today) parts.Add($"date={day:yyyy-MM-dd}");
        if (comp is not null) parts.Add("c=" + comp);
        return "/" + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
    }
}

public record MatchListVm(IReadOnlyList<(DateOnly Day, List<MatchGroupVm> Groups)> Days, bool HasMore, string NextUrl);

public class MatchPageVm
{
    public required MatchDetailVm Detail { get; init; }
    public required string ShareUrl { get; init; }
    public required string ShareText { get; init; }
    public IReadOnlyList<GalleryPhotoVm> Photos { get; init; } = [];
    public IReadOnlyList<ArticleCardVm> News { get; init; } = [];
    public MatchPredictionBlock? Prediction { get; init; }
    public VoteBlock? Vote { get; init; }
}

public class StandingsPageVm
{
    public required PublicContext Ctx { get; init; }
    public required CompetitionVm Competition { get; init; }
    public IReadOnlyList<PhaseStandingsVm> Phases { get; init; } = [];
    public PhaseStandingsVm? Current { get; init; }
}

public class NewsIndexVm
{
    public required NewsFilter Filter { get; init; }
    public required NewsPageData Page { get; init; }
    public ArticleCardVm? Featured { get; init; }
    public IReadOnlyList<CategoryVm> Categories { get; init; } = [];
    /// <summary>Libellé du filtre mot-clé ou équipe (bandeau « Filtré par »).</summary>
    public string? FilterLabel { get; init; }
}

public class ArticlePageVm
{
    public required ArticleDetailVm Article { get; init; }
    public required string ShareUrl { get; init; }
    public required CommentsPartVm Comments { get; init; }
}

public class PredictionsPageVm
{
    public SeasonVm? Season { get; init; }
    public IReadOnlyList<UpcomingPrediction> Upcoming { get; init; } = [];
    public Leaderboard? Board { get; init; }
}

/// <summary>Bloc « Pronostic » de la fiche match (rafraîchi par htmx après envoi).</summary>
public record PredictionPartVm(int MatchId, MatchPredictionBlock? Block, MatchRowVm? Match, string? Error = null, bool Saved = false);

/// <summary>Ligne de la page Pronostics : un match à venir et le pronostic de l'utilisateur.</summary>
public record PredictionRowVm(MatchRowVm Match, PredictionVm? Mine, string? Error = null, bool Saved = false);

public record VotePartVm(int MatchId, VoteBlock? Block, string? Error = null);

public record CommentsPartVm(int ArticleId, bool Allowed, IReadOnlyList<CommentVm> Items, string? Message = null, string? Error = null, string? Draft = null);
