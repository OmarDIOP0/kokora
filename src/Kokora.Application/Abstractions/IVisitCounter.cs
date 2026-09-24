namespace Kokora.Application.Abstractions;

/// <summary>Compteur anonyme de pages vues par rubrique (voir DailyVisit).</summary>
public interface IVisitCounter
{
    void Count(string section);
}
