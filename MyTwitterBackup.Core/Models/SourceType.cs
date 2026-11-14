namespace MyTwitterBackup.Core.Models;

/// <summary>Where a post was downloaded from. Stored as an integer in the database.</summary>
public enum SourceType
{
    Bookmark = 1,
    Like = 2,
    Single = 3,
}
