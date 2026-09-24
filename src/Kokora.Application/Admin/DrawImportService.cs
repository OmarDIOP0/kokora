using System.Text.RegularExpressions;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Kokora.Domain.Competitions;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public record DrawReport(int ClubsCreated, int ClubsFound, int Groups, IReadOnlyList<string> Lines);

/// <summary>
/// Import du tirage au sort des poules, collé tel quel depuis l'affiche :
/// <code>
/// Zone 5A
/// Poule A : Thiossane, Diamono, Pikine, Natangué, Mbaxaal
/// Poule B : Médine, Avenir, …
/// </code>
/// Une ligne sans « : » désigne la compétition (« Zone 5A », « Zonale 5B » ou son nom exact) ; chaque ligne « Poule … : »
/// crée ou met à jour la poule dans la phase de poules. Les équipes inconnues sont créées (préfixe « ASC », zone de la compétition).
/// </summary>
public partial class DrawImportService(IAppDbContext db, CompetitionAdminService competitions, CompetitionCache cache)
{
    private static readonly string[] Palette =
    [
        "#0E6B3A", "#C62828", "#1A237E", "#F9A825", "#6A1B9A", "#00838F", "#4E342E", "#2E7D32", "#AD1457", "#263238",
        "#EF6C00", "#1565C0", "#558B2F", "#B71C1C", "#37474F", "#00695C", "#283593", "#8D6E63", "#5D4037", "#00897B"
    ];

    [GeneratedRegex(@"\b(\d+\s*[A-Za-z])\s*$")]
    private static partial Regex ZoneCode();

    public async Task<DrawReport> ImportAsync(int seasonId, string text, CancellationToken ct = default)
    {
        var comps = await db.Competitions.Include(c => c.Phases).Where(c => c.SeasonId == seasonId).OrderBy(c => c.Order).ToListAsync(ct);
        if (comps.Count == 0) throw new BusinessRuleException("Créez d'abord les compétitions de la saison (bouton « Structure type »).");
        var clubs = await db.Clubs.ToListAsync(ct);
        int created = 0, found = 0, groups = 0;
        var log = new List<string>();
        Competition? current = null;
        string? zone = null;

        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim().Trim('•', '-', '*', '·').Trim();
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                (current, zone) = FindCompetition(comps, line);
                if (current is null) throw new BusinessRuleException($"Compétition introuvable pour « {line} ».", "Text");
                log.Add(current.Name);
                continue;
            }
            if (current is null) throw new BusinessRuleException("Indiquez la compétition (ex. « Zone 5A ») avant la première poule.", "Text");

            var groupName = line[..colon].Trim();
            var names = line[(colon + 1)..].Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(n => Tidy(n.Trim())).Where(n => n.Length > 0).ToList();
            if (names.Count < 2) throw new BusinessRuleException($"« {groupName} » : au moins deux équipes, séparées par des virgules.", "Text");

            var ids = new List<int>();
            foreach (var name in names)
            {
                var club = Match(clubs, name);
                if (club is null)
                {
                    var full = name.StartsWith("ASC ", StringComparison.OrdinalIgnoreCase) ? name : $"ASC {name}";
                    club = new Club
                    {
                        Name = full, ShortName = name.Length <= 40 ? name : name[..40], Zone = zone,
                        PrimaryColor = Palette[clubs.Count % Palette.Length], SecondaryColor = "#FFFFFF",
                        Slug = await Slug.UniqueAsync(full, s => Task.FromResult(clubs.Any(c => c.Slug == s)))
                    };
                    db.Clubs.Add(club);
                    clubs.Add(club);
                    await db.SaveChangesAsync(ct);
                    created++;
                }
                else found++;
                ids.Add(club.Id);
            }

            var phase = current.Phases.Where(p => p.Type == PhaseType.League).OrderBy(p => p.Order).FirstOrDefault();
            if (phase is null)
            {
                var phaseId = await competitions.SavePhaseAsync(new PhaseInput { CompetitionId = current.Id, Name = "Phase de poules", Type = PhaseType.League }, ct);
                phase = await db.Phases.FirstAsync(p => p.Id == phaseId, ct);
                current.Phases.Add(phase);
            }
            var existing = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.PhaseId == phase.Id && g.Name == groupName, ct);
            await competitions.SaveGroupAsync(new GroupInput { Id = existing?.Id ?? 0, PhaseId = phase.Id, Name = groupName, ClubIds = ids }, ct);
            groups++;
            log.Add($"  {groupName} : {string.Join(", ", names)}");
        }
        foreach (var c in comps) cache.Invalidate(c.Id);
        return new DrawReport(created, found, groups, log);
    }

    /// <summary>« Zone 5A » ou « Zonale 5A » → la zonale de cette zone ; sinon le nom exact de la compétition.</summary>
    private static (Competition?, string?) FindCompetition(List<Competition> comps, string heading)
    {
        var slug = Slug.From(heading);
        var exact = comps.FirstOrDefault(c => c.Slug == slug || Slug.From(c.Name) == slug);
        var code = ZoneCode().Match(heading) is { Success: true } m ? m.Groups[1].Value.Replace(" ", "").ToUpperInvariant() : null;
        if (exact is not null) return (exact, code);
        if (code is null) return (null, null);
        var zonal = comps.FirstOrDefault(c => c.Name.StartsWith("Zonale", StringComparison.OrdinalIgnoreCase)
                                              && Slug.From(c.Name).EndsWith(code.ToLowerInvariant()));
        return (zonal, code);
    }

    /// <summary>Noms recopiés en majuscules depuis une affiche (« KEUR GONDÉ ») : écrits « Keur Gondé ».</summary>
    private static string Tidy(string name) =>
        name.Any(char.IsLower) ? name : Common.KokoraTime.Fr.TextInfo.ToTitleCase(name.ToLower(Common.KokoraTime.Fr));

    /// <summary>Équipe existante : même nom, avec ou sans « ASC », accents et majuscules ignorés.</summary>
    private static Club? Match(List<Club> clubs, string name)
    {
        var slug = Slug.From(name);
        return clubs.FirstOrDefault(c => Slug.From(c.Name) == slug || Slug.From(c.Name) == $"asc-{slug}" || Slug.From(c.ShortName) == slug);
    }
}
