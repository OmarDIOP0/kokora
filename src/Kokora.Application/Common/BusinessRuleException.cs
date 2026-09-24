namespace Kokora.Application.Common;

/// <summary>Règle métier non respectée : le message est affiché tel quel à l'utilisateur.</summary>
public class BusinessRuleException(string message, string? field = null) : Exception(message)
{
    /// <summary>Champ du formulaire concerné (null = erreur générale).</summary>
    public string? Field { get; } = field;
}

public class NotFoundException(string what) : Exception($"{what} introuvable.");
