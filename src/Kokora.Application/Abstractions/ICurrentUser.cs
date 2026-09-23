namespace Kokora.Application.Abstractions;

/// <summary>Utilisateur de la requête en cours (implémenté dans la couche Web).</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    string? IpAddress { get; }
    bool IsInRole(string role);
}

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Editor = "Rédacteur";
    public const string User = "Utilisateur";

    public const string AdminOrAbove = SuperAdmin + "," + Admin;
    public const string Staff = SuperAdmin + "," + Admin + "," + Editor;

    public static readonly string[] All = [SuperAdmin, Admin, Editor, User];
}
