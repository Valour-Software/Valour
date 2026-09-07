using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Database.Migrations;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class SentryRoleSchemaTests(LoginTestFixture fixture)
{
    [Fact]
    public async Task LegacyRoleColumns_AcceptCurrentModelTextAfterMigration()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // A temporary table shadows the real table only on this connection.
        await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE planet_roles (name varchar(50), color varchar(8)) ON COMMIT DROP;");
        var migration = new NormalizePlanetRoleTextColumns();
        foreach (var operation in migration.UpOperations.Cast<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
        var name = new string('n', 51);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO planet_roles (name, color) VALUES ({name}, {"#11223344"});");
        var stored = await db.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM planet_roles").SingleAsync();
        Assert.Equal(name, stored);
        await transaction.RollbackAsync();
    }
}
