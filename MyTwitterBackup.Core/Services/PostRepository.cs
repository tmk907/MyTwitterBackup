using Microsoft.EntityFrameworkCore;
using MyTwitterBackup.Core.Database;
using MyTwitterBackup.Core.Models;

namespace MyTwitterBackup.Core.Services;

public sealed record PostSummary(string Id, string UserName, string DisplayName, DateTime CreatedAt, string Snippet, int MediaCount);

/// <summary>Read access to downloaded posts for the "Downloaded" page.</summary>
public sealed class PostRepository(IAppDbContextFactory dbFactory)
{
    public async Task<List<PostSummary>> GetPostsAsync(SourceType sourceType)
    {
        await using var db = dbFactory.Create();
        var rows = await db.Posts.AsNoTracking()
            .Where(p => p.Sources.Any(s => s.SourceType == sourceType))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.UserName,
                p.DisplayName,
                p.CreatedAt,
                Text = p.Text.Length > 300 ? p.Text.Substring(0, 300) : p.Text,
                MediaCount = p.Media.Count,
            })
            .ToListAsync();

        return rows
            .Select(r => new PostSummary(r.Id, r.UserName, r.DisplayName, r.CreatedAt,
                DownloadQueueService.Snippet(r.Text, 120), r.MediaCount))
            .ToList();
    }

    public async Task<Post?> GetPostAsync(string id)
    {
        await using var db = dbFactory.Create();
        return await db.Posts.AsNoTracking()
            .Include(p => p.Media.OrderBy(m => m.Index))
            .Include(p => p.Sources)
            .Include(p => p.QuotedPost)
                .ThenInclude(q => q!.Media.OrderBy(m => m.Index))
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);
    }
}
