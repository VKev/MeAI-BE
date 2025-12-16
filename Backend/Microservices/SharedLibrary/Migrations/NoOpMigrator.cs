using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace SharedLibrary.Migrations
{
    public sealed class NoOpMigrator : IMigrator
    {
        private readonly ILogger<NoOpMigrator> _logger;

        public NoOpMigrator(ILogger<NoOpMigrator> logger)
        {
            _logger = logger;
        }

        private void LogSkip(string? targetMigration)
        {
            _logger.LogInformation(
                "Skipping EF Core migration to {TargetMigration} because automatic migrations are disabled.",
                targetMigration ?? "latest");
        }

        public void Migrate(string? targetMigration = null)
        {
            LogSkip(targetMigration);
        }

        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
        {
            LogSkip(targetMigration);
            return Task.CompletedTask;
        }

        public string GenerateScript(
            string? fromMigration = null,
            string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            _logger.LogInformation(
                "Skipping EF Core migration script from {FromMigration} to {ToMigration}; migrations disabled.",
                fromMigration ?? "initial",
                toMigration ?? "latest");
            return string.Empty;
        }

        public Task<string> GenerateScriptAsync(
            string? fromMigration = null,
            string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(GenerateScript(fromMigration, toMigration, options));
        }

        public bool HasPendingModelChanges()
        {
            _logger.LogInformation("Skipping pending-model-change check because automatic migrations are disabled.");
            return false;
        }

        public Task<bool> HasPendingModelChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
