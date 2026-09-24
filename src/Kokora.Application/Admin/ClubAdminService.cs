using Kokora.Application.Abstractions;
using Kokora.Application.Common;
using Kokora.Domain.Clubs;
using Microsoft.EntityFrameworkCore;

namespace Kokora.Application.Admin;

public class ClubAdminService(IAppDbContext db, IImageStore images)
{
    public Task<List<Club>> ListAsync(string? search, string? zone, CancellationToken ct = default)
    {
        var q = db.Clubs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(s) || c.ShortName.ToLower().Contains(s) || (c.Neighborhood ?? "").ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(zone)) q = q.Where(c => c.Zone == zone);
        return q.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Club> GetAsync(int id, CancellationToken ct = default) =>
        await db.Clubs.FindAsync([id], ct) ?? throw new NotFoundException("Équipe");

    public static ClubInput ToInput(Club c) => new()
    {
        Id = c.Id, Name = c.Name, ShortName = c.ShortName, PrimaryColor = c.PrimaryColor, SecondaryColor = c.SecondaryColor,
        Neighborhood = c.Neighborhood, Zone = c.Zone, ManagerName = c.ManagerName, ContactPhone = c.ContactPhone,
        FoundedYear = c.FoundedYear, IsActive = c.IsActive
    };

    public async Task<int> SaveAsync(ClubInput input, Stream? logo, CancellationToken ct = default)
    {
        var name = input.Name.Trim();
        if (await db.Clubs.AnyAsync(c => c.Id != input.Id && c.Name.ToLower() == name.ToLower(), ct))
            throw new BusinessRuleException("Une équipe porte déjà ce nom.", nameof(input.Name));

        var club = input.Id == 0 ? new Club() : await GetAsync(input.Id, ct);
        club.Name = name;
        club.ShortName = input.ShortName.Trim();
        club.PrimaryColor = input.PrimaryColor.ToUpperInvariant();
        club.SecondaryColor = input.SecondaryColor.ToUpperInvariant();
        club.Neighborhood = Clean(input.Neighborhood);
        club.Zone = Clean(input.Zone)?.ToUpperInvariant();
        club.ManagerName = Clean(input.ManagerName);
        club.ContactPhone = Clean(input.ContactPhone);
        club.FoundedYear = input.FoundedYear;
        club.IsActive = input.IsActive;

        if (input.Id == 0)
        {
            club.Slug = await Slug.UniqueAsync(club.Name, s => db.Clubs.AnyAsync(c => c.Slug == s, ct));
            db.Clubs.Add(club);
        }

        string? oldLogo = null;
        if (logo is not null)
        {
            var urls = await images.SaveAsync(logo, "logos", Slug.From(club.Name, 40), ImagePresets.Logo, ct);
            oldLogo = club.LogoPath;
            club.LogoPath = urls[0];
        }
        else if (input.RemoveLogo)
        {
            oldLogo = club.LogoPath;
            club.LogoPath = null;
        }

        await db.SaveChangesAsync(ct);
        if (oldLogo is not null) await images.DeleteAsync(oldLogo);
        return club.Id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var club = await GetAsync(id, ct);
        if (await db.Matches.AnyAsync(m => m.HomeClubId == id || m.AwayClubId == id, ct))
            throw new BusinessRuleException("Cette équipe a des matchs : désactivez-la plutôt que de la supprimer.");
        if (await db.GroupTeams.AnyAsync(t => t.ClubId == id, ct))
            throw new BusinessRuleException("Cette équipe est inscrite dans une poule : retirez-la d'abord.");
        db.Clubs.Remove(club);
        await db.SaveChangesAsync(ct);
        await images.DeleteAsync(club.LogoPath);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public class VenueAdminService(IAppDbContext db)
{
    public Task<List<Stadium>> StadiumsAsync(CancellationToken ct = default) =>
        db.Stadiums.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

    public Task<List<Referee>> RefereesAsync(CancellationToken ct = default) =>
        db.Referees.AsNoTracking().OrderBy(r => r.FullName).ToListAsync(ct);

    public async Task<int> SaveStadiumAsync(StadiumInput input, CancellationToken ct = default)
    {
        var s = input.Id == 0 ? new Stadium() : await db.Stadiums.FindAsync([input.Id], ct) ?? throw new NotFoundException("Stade");
        s.Name = input.Name.Trim();
        s.Neighborhood = string.IsNullOrWhiteSpace(input.Neighborhood) ? null : input.Neighborhood.Trim();
        s.Capacity = input.Capacity;
        if (input.Id == 0) db.Stadiums.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task<int> SaveRefereeAsync(RefereeInput input, CancellationToken ct = default)
    {
        var r = input.Id == 0 ? new Referee() : await db.Referees.FindAsync([input.Id], ct) ?? throw new NotFoundException("Arbitre");
        r.FullName = input.FullName.Trim();
        r.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        if (input.Id == 0) db.Referees.Add(r);
        await db.SaveChangesAsync(ct);
        return r.Id;
    }

    /// <summary>Les matchs gardent leur historique : le stade/arbitre y est simplement retiré.</summary>
    public async Task DeleteStadiumAsync(int id, CancellationToken ct = default)
    {
        var s = await db.Stadiums.FindAsync([id], ct) ?? throw new NotFoundException("Stade");
        db.Stadiums.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteRefereeAsync(int id, CancellationToken ct = default)
    {
        var r = await db.Referees.FindAsync([id], ct) ?? throw new NotFoundException("Arbitre");
        db.Referees.Remove(r);
        await db.SaveChangesAsync(ct);
    }
}
