using MyTwitterBackup.Core.Services;

namespace MyTwitterBackup.Core.Database;

public interface IAppDbContextFactory
{
    string DatabasePath { get; }
    AppDbContext Create();
}

/// <summary>Creates short-lived contexts for the database in the downloads folder.</summary>
public sealed class AppDbContextFactory(IAppSettings settings) : IAppDbContextFactory
{
    public string DatabasePath => Path.Combine(settings.DownloadsFolder, AppDbContext.DatabaseFileName);

    public AppDbContext Create() => new(DatabasePath);
}
