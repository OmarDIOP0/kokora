using System.Globalization;
using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Kokora.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public record PlayerListItem(int Id, string FullName, string? Nickname, PlayerPosition Position, string? PhotoPath,
    int? ClubId, string? ClubName, int? ShirtNumber);

/// <summary>Une ligne lue dans un fichier d'import (CSV ou Excel). Toutes les valeurs sont du texte brut.</summary>
public record PlayerImportRow(int Line, string? FirstName, string? LastName, string? Nickname, string? Position,
    string? Club, string? ShirtNumber, string? BirthDate, string? License);

public record ImportReport(int Created, int Updated, IReadOnlyList<string> Errors);

public class PlayerAdminService(IAppDbContext db, IImageStore images)
{
    public async Task<(List<PlayerListItem> Items, int Total)> ListAsync(int seasonId, string? search, int? clubId,
        int page, int pageSize, CancellationToken ct = default)
    {
        var q = db.Players.AsNoTracking()
            .Select(p => new { Player = p, Member = p.Memberships.FirstOrDefault(m => m.SeasonId == seasonId) });
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(x => (x.Player.FirstName + " " + x.Player.LastName).ToLower().Contains(s)
                || (x.Player.Nickname ?? "").ToLower().Contains(s));
        }
        if (clubId == -1) q = q.Where(x => x.Member == null);
        else if (clubId is > 0) q = q.Where(x => x.Member != null && x.Member.ClubId == clubId);

        var total = await q.CountAsync(ct);
        // Effectif d'une équipe : par numéro de maillot ; sinon ordre alphabétique.
        var ordered = clubId is > 0
            ? q.OrderBy(x => x.Member!.ShirtNumber == null).ThenBy(x => x.Member!.ShirtNumber).ThenBy(x => x.Player.LastName)
            : q.OrderBy(x => x.Player.LastName).ThenBy(x => x.Player.FirstName);
        var items = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PlayerListItem(x.Player.Id, x.Player.FirstName + " " + x.Player.LastName, x.Player.Nickname,
                x.Player.Position, x.Player.PhotoPath, x.Member != null ? x.Member.ClubId : null,
                x.Member != null ? x.Member.Club.Name : null, x.Member != null ? x.Member.ShirtNumber : null))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<(Player Player, SquadMember? Member)> GetAsync(int id, int seasonId, CancellationToken ct = default)
    {
        var p = await db.Players.FindAsync([id], ct) ?? throw new NotFoundException("Joueur");
        var m = await db.SquadMembers.FirstOrDefaultAsync(x => x.PlayerId == id && x.SeasonId == seasonId, ct);
        return (p, m);
    }

    public static PlayerInput ToInput(Player p, SquadMember? m) => new()
    {
        Id = p.Id, FirstName = p.FirstName, LastName = p.LastName, Nickname = p.Nickname, Position = p.Position,
        BirthDate = p.BirthDate, ClubId = m?.ClubId, ShirtNumber = m?.ShirtNumber, LicenseNumber = m?.LicenseNumber,
        IsCaptain = m?.IsCaptain ?? false
    };

    public async Task<int> SaveAsync(PlayerInput input, int seasonId, Stream? photo, CancellationToken ct = default)
    {
        var p = input.Id == 0 ? new Player() : await db.Players.FindAsync([input.Id], ct) ?? throw new NotFoundException("Joueur");
        p.FirstName = Title(input.FirstName);
        p.LastName = Title(input.LastName);
        p.Nickname = string.IsNullOrWhiteSpace(input.Nickname) ? null : input.Nickname.Trim();
        p.Position = input.Position;
        p.BirthDate = input.BirthDate;
        if (input.Id == 0)
        {
            p.Slug = await Slug.UniqueAsync($"{p.FirstName} {p.LastName}", s => db.Players.AnyAsync(x => x.Slug == s, ct));
            db.Players.Add(p);
        }

        await SetMembershipAsync(p, seasonId, input.ClubId, input.ShirtNumber, input.LicenseNumber, input.IsCaptain, ct);

        string? oldPhoto = null;
        if (photo is not null)
        {
            var urls = await images.SaveAsync(photo, "joueurs", Slug.From(p.FullName, 40), ImagePresets.PlayerPhoto, ct);
            oldPhoto = p.PhotoPath;
            p.PhotoPath = urls[0];
        }
        else if (input.RemovePhoto)
        {
            oldPhoto = p.PhotoPath;
            p.PhotoPath = null;
        }

        await db.SaveChangesAsync(ct);
        if (oldPhoto is not null) await images.DeleteAsync(oldPhoto);
        return p.Id;
    }

    private async Task SetMembershipAsync(Player p, int seasonId, int? clubId, int? number, string? license, bool captain, CancellationToken ct)
    {
        var member = p.Id == 0 ? null : await db.SquadMembers.FirstOrDefaultAsync(m => m.PlayerId == p.Id && m.SeasonId == seasonId, ct);
        if (clubId is null)
        {
            if (member is not null) db.SquadMembers.Remove(member);
            return;
        }
        if (number is not null && await db.SquadMembers.AnyAsync(m => m.SeasonId == seasonId && m.ClubId == clubId
                && m.ShirtNumber == number && m.PlayerId != p.Id, ct))
            throw new BusinessRuleException($"Le numéro {number} est déjà attribué dans cette équipe.", nameof(PlayerInput.ShirtNumber));

        if (member is null)
        {
            member = new SquadMember { SeasonId = seasonId, Player = p };
            db.SquadMembers.Add(member);
        }
        member.ClubId = clubId.Value;
        member.ShirtNumber = number;
        member.LicenseNumber = string.IsNullOrWhiteSpace(license) ? null : license.Trim();
        member.IsCaptain = captain;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var p = await db.Players.FindAsync([id], ct) ?? throw new NotFoundException("Joueur");
        if (await db.MatchEvents.AnyAsync(e => e.PlayerId == id || e.AssistPlayerId == id || e.PlayerOutId == id, ct)
            || await db.LineupEntries.AnyAsync(l => l.PlayerId == id, ct))
            throw new BusinessRuleException("Ce joueur apparaît dans des feuilles de match : il ne peut pas être supprimé.");
        db.Players.Remove(p);
        await db.SaveChangesAsync(ct);
        await images.DeleteAsync(p.PhotoPath);
    }

    /// <summary>
    /// Import en masse. Un joueur existant (même prénom + nom + équipe) est mis à jour ; sinon il est créé.
    /// Les lignes invalides sont ignorées et signalées, les autres sont enregistrées.
    /// </summary>
    public async Task<ImportReport> ImportAsync(IReadOnlyList<PlayerImportRow> rows, int seasonId, CancellationToken ct = default)
    {
        var clubs = await db.Clubs.AsNoTracking().Select(c => new { c.Id, c.Name, c.ShortName }).ToListAsync(ct);
        var clubByKey = new Dictionary<string, int>();
        foreach (var c in clubs)
        {
            clubByKey.TryAdd(Key(c.Name), c.Id);
            clubByKey.TryAdd(Key(c.ShortName), c.Id);
        }

        var errors = new List<string>();
        int created = 0, updated = 0;
        var numbersTaken = (await db.SquadMembers.Where(m => m.SeasonId == seasonId && m.ShirtNumber != null)
                .Select(m => new { m.ClubId, m.ShirtNumber, m.PlayerId }).ToListAsync(ct))
            .ToDictionary(x => (x.ClubId, x.ShirtNumber!.Value), x => x.PlayerId);

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.FirstName) || string.IsNullOrWhiteSpace(row.LastName))
            {
                errors.Add($"Ligne {row.Line} : prénom et nom obligatoires.");
                continue;
            }
            int? clubId = null;
            if (!string.IsNullOrWhiteSpace(row.Club))
            {
                if (!clubByKey.TryGetValue(Key(row.Club), out var id))
                {
                    errors.Add($"Ligne {row.Line} : équipe « {row.Club} » inconnue.");
                    continue;
                }
                clubId = id;
            }
            int? number = null;
            if (!string.IsNullOrWhiteSpace(row.ShirtNumber))
            {
                if (!int.TryParse(row.ShirtNumber.Trim(), out var n) || n is < 1 or > 99)
                {
                    errors.Add($"Ligne {row.Line} : numéro « {row.ShirtNumber} » invalide.");
                    continue;
                }
                number = n;
            }
            DateOnly? birth = null;
            if (!string.IsNullOrWhiteSpace(row.BirthDate))
            {
                if (!TryParseDate(row.BirthDate, out var d))
                {
                    errors.Add($"Ligne {row.Line} : date de naissance « {row.BirthDate} » invalide (jj/mm/aaaa).");
                    continue;
                }
                birth = d;
            }

            var first = Title(row.FirstName);
            var last = Title(row.LastName);
            var existing = await db.Players
                .Where(p => p.FirstName.ToLower() == first.ToLower() && p.LastName.ToLower() == last.ToLower())
                .Where(p => clubId == null || p.Memberships.Any(m => m.SeasonId == seasonId && m.ClubId == clubId))
                .FirstOrDefaultAsync(ct);

            // Vérifié avant toute modification pour qu'une ligne refusée ne laisse aucune trace.
            if (clubId is not null && number is not null && numbersTaken.TryGetValue((clubId.Value, number.Value), out var owner)
                && (existing is null || owner != existing.Id))
            {
                errors.Add($"Ligne {row.Line} : le numéro {number} est déjà pris dans cette équipe.");
                continue;
            }

            var player = existing ?? new Player { FirstName = first, LastName = last };
            if (!string.IsNullOrWhiteSpace(row.Nickname)) player.Nickname = row.Nickname.Trim();
            if (ParsePosition(row.Position) is { } pos) player.Position = pos;
            if (birth is not null) player.BirthDate = birth;

            if (existing is null)
            {
                player.Slug = await Slug.UniqueAsync($"{first} {last}", s => db.Players.AnyAsync(x => x.Slug == s, ct));
                db.Players.Add(player);
                created++;
            }
            else updated++;

            if (clubId is not null)
                await SetMembershipAsync(player, seasonId, clubId, number, row.License, false, ct);
            await db.SaveChangesAsync(ct);
            if (clubId is not null && number is not null) numbersTaken[(clubId.Value, number.Value)] = player.Id;
        }
        return new ImportReport(created, updated, errors);
    }

    public static PlayerPosition? ParsePosition(string? value) => Key(value ?? "") switch
    {
        "g" or "gb" or "gardien" or "goal" or "gardien de but" => PlayerPosition.Goalkeeper,
        "d" or "def" or "defenseur" or "arriere" => PlayerPosition.Defender,
        "m" or "mil" or "milieu" or "milieu de terrain" => PlayerPosition.Midfielder,
        "a" or "att" or "attaquant" or "avant" or "buteur" => PlayerPosition.Forward,
        _ => null
    };

    private static bool TryParseDate(string s, out DateOnly date)
    {
        string[] formats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy"];
        if (DateOnly.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateTime.TryParse(s, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }
        return false;
    }

    private static string Key(string s) => Slug.From(s).Replace('-', ' ');

    private static string Title(string s)
    {
        s = string.Join(' ', s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // Respecte la casse saisie si elle est mixte ; sinon met en forme « Moussa Ndiaye ».
        if (s.Any(char.IsLower) && s.Any(char.IsUpper)) return s;
        return CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(s.ToLowerInvariant());
    }
}
