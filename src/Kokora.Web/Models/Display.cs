using Kokora.Domain.Enums;

namespace Kokora.Web.Models;

/// <summary>Libellés français des statuts et résultats.</summary>
public static class Display
{
    public static string StatusLabel(MatchStatus s) => s switch
    {
        MatchStatus.Scheduled => "Programmé",
        MatchStatus.Postponed => "Reporté",
        MatchStatus.Live => "En direct",
        MatchStatus.HalfTime => "Mi-temps",
        MatchStatus.Finished => "Terminé",
        MatchStatus.Forfeit => "Forfait",
        MatchStatus.Abandoned => "Arrêté",
        MatchStatus.Replay => "À rejouer",
        MatchStatus.UnderReview => "Sous réserve",
        _ => ""
    };

    /// <summary>Libellé court pour la colonne horaire d'une ligne de match.</summary>
    public static string StatusShort(MatchStatus s) => s switch
    {
        MatchStatus.Postponed => "Rep.",
        MatchStatus.HalfTime => "MT",
        MatchStatus.Finished => "Fin",
        MatchStatus.Forfeit => "Forf.",
        MatchStatus.Abandoned => "Arrêté",
        MatchStatus.Replay => "Rejouer",
        MatchStatus.UnderReview => "Réserve",
        _ => ""
    };

    /// <summary>Classe CSS du badge de statut.</summary>
    public static string StatusBadge(MatchStatus s) => s switch
    {
        MatchStatus.Live or MatchStatus.HalfTime => "live",
        MatchStatus.Postponed or MatchStatus.UnderReview or MatchStatus.Replay => "warn",
        MatchStatus.Forfeit or MatchStatus.Abandoned => "loss",
        _ => ""
    };

    public static string FormClass(char c) => c switch { 'V' => "w", 'N' => "d", _ => "l" };
    public static string FormTitle(char c) => c switch { 'V' => "Victoire", 'N' => "Nul", _ => "Défaite" };

    public static string Signed(int n) => n > 0 ? $"+{n}" : n.ToString();
}
