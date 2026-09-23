using Kokora.Domain.Common;

namespace Kokora.Domain.Competitions;

public class Season : Entity, IAuditable, IDemoData
{
    public int Year { get; set; }
    public string Name { get; set; } = "";
    public bool IsCurrent { get; set; }
    public bool IsDemo { get; set; }

    public List<Competition> Competitions { get; set; } = [];
}
