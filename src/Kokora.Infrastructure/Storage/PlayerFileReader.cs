using System.Globalization;
using System.Text;
using Kokora.Application.Abstractions;
using Kokora.Application.Admin;
using Kokora.Application.Common;
using MiniExcelLibs;
using MiniExcelLibs.Csv;

namespace Kokora.Infrastructure.Storage;

public class PlayerFileReader : IPlayerFileReader
{
    private const int MaxRows = 2000;

    // En-têtes acceptés (normalisés sans accents, en minuscules) → champ.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["prenom"] = "first", ["prenoms"] = "first", ["first name"] = "first", ["firstname"] = "first",
        ["nom"] = "last", ["nom de famille"] = "last", ["last name"] = "last", ["lastname"] = "last",
        ["surnom"] = "nick", ["nickname"] = "nick",
        ["poste"] = "pos", ["position"] = "pos",
        ["equipe"] = "club", ["asc"] = "club", ["club"] = "club",
        ["numero"] = "num", ["n"] = "num", ["no"] = "num", ["maillot"] = "num", ["dossard"] = "num",
        ["date de naissance"] = "birth", ["naissance"] = "birth", ["ne le"] = "birth",
        ["licence"] = "lic", ["n licence"] = "lic", ["numero de licence"] = "lic",
    };

    public IReadOnlyList<PlayerImportRow> Read(Stream file, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        buffer.Position = 0;

        IEnumerable<IDictionary<string, object?>> rows;
        if (ext == ".xlsx")
        {
            rows = buffer.Query(useHeaderRow: true, excelType: ExcelType.XLSX).Cast<IDictionary<string, object?>>();
        }
        else if (ext == ".csv" || ext == ".txt")
        {
            // Excel en français enregistre les CSV avec « ; » : on détecte le séparateur sur la première ligne.
            var firstLine = new StreamReader(buffer, Encoding.UTF8, true, leaveOpen: true).ReadLine() ?? "";
            buffer.Position = 0;
            var separator = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';
            rows = buffer.Query(useHeaderRow: true, excelType: ExcelType.CSV,
                configuration: new CsvConfiguration { Seperator = separator }).Cast<IDictionary<string, object?>>();
        }
        else
        {
            throw new BusinessRuleException("Format non pris en charge : utilisez un fichier .xlsx ou .csv.");
        }

        var result = new List<PlayerImportRow>();
        var line = 1; // la ligne 1 est l'en-tête
        foreach (var raw in rows)
        {
            line++;
            if (result.Count >= MaxRows) throw new BusinessRuleException($"Fichier trop long ({MaxRows} lignes maximum).");
            var map = new Dictionary<string, string?>();
            foreach (var (key, value) in raw)
            {
                var norm = Slug.From(key ?? "").Replace('-', ' ');
                if (Aliases.TryGetValue(norm, out var field)) map[field] = Text(value);
            }
            if (map.Values.All(string.IsNullOrWhiteSpace)) continue; // ligne vide
            result.Add(new PlayerImportRow(line, map.GetValueOrDefault("first"), map.GetValueOrDefault("last"),
                map.GetValueOrDefault("nick"), map.GetValueOrDefault("pos"), map.GetValueOrDefault("club"),
                map.GetValueOrDefault("num"), map.GetValueOrDefault("birth"), map.GetValueOrDefault("lic")));
        }
        if (result.Count > 0 && result.All(r => r.FirstName is null && r.LastName is null))
            throw new BusinessRuleException("Colonnes « Prénom » et « Nom » introuvables : utilisez le modèle fourni.");
        return result;
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateTime d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        double d when d == Math.Floor(d) => ((long)d).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim()
    };
}
