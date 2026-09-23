using Kokora.Domain.Common;

namespace Kokora.Domain.Clubs;

public class Stadium : Entity, IAuditable, IDemoData
{
    public string Name { get; set; } = "";
    public string? Neighborhood { get; set; }
    public int? Capacity { get; set; }
    public bool IsDemo { get; set; }
}

public class Referee : Entity, IAuditable, IDemoData
{
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public bool IsDemo { get; set; }
}
