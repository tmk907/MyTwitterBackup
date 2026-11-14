using Microsoft.EntityFrameworkCore;
using Serilog;

namespace MyTwitterBackup.Core.Database;

public sealed class DatabaseInitializer(IAppDbContextFactory factory)
{
    /// <summary>Creates the database (and its folder) if needed. Safe to call many times.</summary>
    public void EnsureCreated()
    {
        var path = factory.DatabasePath;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        using var db = factory.Create();
        db.Database.EnsureCreated();
        // WAL lets the UI read while the download worker writes.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        Log.Information("Database ready at {Path}", path);
    }
}
