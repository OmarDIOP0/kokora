using Kokora.Application.Admin;

namespace Kokora.Application.Abstractions;

/// <summary>Lit un fichier de joueurs (CSV ou Excel .xlsx) en lignes brutes.</summary>
public interface IPlayerFileReader
{
    /// <summary>Colonnes reconnues (insensible à la casse et aux accents).</summary>
    public static readonly string[] Columns = ["Prénom", "Nom", "Surnom", "Poste", "Équipe", "Numéro", "Date de naissance", "Licence"];

    IReadOnlyList<PlayerImportRow> Read(Stream file, string fileName);
}
