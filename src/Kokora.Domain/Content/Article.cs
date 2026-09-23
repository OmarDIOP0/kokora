using Kokora.Domain.Clubs;
using Kokora.Domain.Common;
using Kokora.Domain.Enums;
using Kokora.Domain.Matches;

namespace Kokora.Domain.Content;

public class Article : Entity, IAuditable, IDemoData
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Summary { get; set; }
    /// <summary>HTML nettoyé (liste blanche) produit par l'éditeur.</summary>
    public string Body { get; set; } = "";
    public string? CoverImagePath { get; set; }
    public int? CategoryId { get; set; }
    public ArticleCategory? Category { get; set; }
    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;
    public DateTimeOffset? PublishedAt { get; set; }
    public bool IsFeatured { get; set; }
    /// <summary>Info importante : déclenche une notification push à la publication.</summary>
    public bool IsImportant { get; set; }
    public bool AllowComments { get; set; } = true;
    public string? AuthorId { get; set; }
    public string? AuthorName { get; set; }
    public bool IsDemo { get; set; }

    public List<Tag> Tags { get; set; } = [];
    public List<Club> Clubs { get; set; } = [];
    public List<Match> Matches { get; set; } = [];
    public List<Comment> Comments { get; set; } = [];
}

public class ArticleCategory : Entity, IAuditable, IDemoData
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public int Order { get; set; }
    public bool IsDemo { get; set; }
}

public class Tag : Entity, IDemoData
{
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool IsDemo { get; set; }
    public List<Article> Articles { get; set; } = [];
}

public class Photo : Entity, IAuditable, IDemoData
{
    public string Path { get; set; } = "";
    public string? ThumbnailPath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? Caption { get; set; }
    public string? Credit { get; set; }
    public int? MatchId { get; set; }
    public Match? Match { get; set; }
    public int? ArticleId { get; set; }
    public Article? Article { get; set; }
    public int Order { get; set; }
    public bool IsDemo { get; set; }
}

public class Comment : Entity, IAuditable
{
    public int ArticleId { get; set; }
    public Article Article { get; set; } = null!;
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string Body { get; set; } = "";
    public CommentStatus Status { get; set; } = CommentStatus.Pending;
}
