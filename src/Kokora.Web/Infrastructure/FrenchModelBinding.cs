using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Kokora.Web.Infrastructure;

/// <summary>Messages de liaison de modèle en français (valeurs non numériques, champs manquants…).</summary>
public static class FrenchModelBinding
{
    public static void Configure(DefaultModelBindingMessageProvider p)
    {
        p.SetAttemptedValueIsInvalidAccessor((value, field) => $"La valeur « {value} » n'est pas valide pour {field}.");
        p.SetMissingBindRequiredValueAccessor(field => $"Le champ {field} est obligatoire.");
        p.SetMissingKeyOrValueAccessor(() => "Valeur obligatoire.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => "Le formulaire est vide.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"La valeur « {value} » n'est pas valide.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Valeur invalide.");
        p.SetNonPropertyValueMustBeANumberAccessor(() => "Doit être un nombre.");
        p.SetUnknownValueIsInvalidAccessor(field => $"Valeur invalide pour {field}.");
        p.SetValueIsInvalidAccessor(value => $"La valeur « {value} » n'est pas valide.");
        p.SetValueMustBeANumberAccessor(field => $"{field} doit être un nombre.");
        p.SetValueMustNotBeNullAccessor(value => $"La valeur « {value} » est obligatoire.");
    }
}
