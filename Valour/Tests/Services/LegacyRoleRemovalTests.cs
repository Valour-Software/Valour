using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Valour.Database.Context;
using Valour.Database.Migrations;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class LegacyRoleRemovalTests(LoginTestFixture fixture)
{
    private static async Task CreateLegacySchema(ValourDb db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TEMP TABLE planets (id bigint PRIMARY KEY, version integer) ON COMMIT DROP;
            CREATE TEMP TABLE planet_roles (id bigint PRIMARY KEY, planet_id bigint,
                is_default boolean, position bigint, local_index integer) ON COMMIT DROP;
            CREATE TEMP TABLE planet_members (id bigint PRIMARY KEY, planet_id bigint,
                rf0 bigint, rf1 bigint, rf2 bigint, rf3 bigint) ON COMMIT DROP;
            CREATE TEMP TABLE planet_role_members (member_id bigint REFERENCES planet_members(id),
                role_id bigint REFERENCES planet_roles(id)) ON COMMIT DROP;
            """);
    }

    private static async Task ApplyMigration(ValourDb db)
    {
        var migration = new DropLegacyPlanetRoleMembers();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    [Fact]
    public async Task Drop_PreservesLegacyMembershipAcrossAllWordsAndLeavesCurrentFlagsUntouched()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CreateLegacySchema(db);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO planets VALUES (1, 0), (2, 1), (3, 0);
            INSERT INTO planet_roles
                SELECT i + 100, 1, i = 0, 256 - i, 0 FROM generate_series(0, 255) i;
            INSERT INTO planet_roles VALUES (999, 2, false, 1, 17), (1000, 3, true, 1, 0);
            INSERT INTO planet_members VALUES (10, 1, 0, 0, 0, 0), (11, 1, 0, 0, 0, 0),
                (20, 2, 131072, 8, 16, 32), (30, 3, 1, 2, 4, 8);
            INSERT INTO planet_role_members SELECT 10, i + 100 FROM
                unnest(ARRAY[0,63,64,127,128,191,192,255]) i;
            INSERT INTO planet_role_members VALUES (20, 999);
            """);
        await ApplyMigration(db);
        var flags = await db.Database.SqlQueryRaw<string>("""
            SELECT concat_ws(',', rf0, rf1, rf2, rf3) AS "Value" FROM planet_members WHERE id = 10
            """).SingleAsync();
        Assert.Equal(string.Join(',', Enumerable.Repeat((long.MinValue + 1).ToString(), 4)), flags);
        Assert.Equal("131072,8,16,32", await db.Database.SqlQueryRaw<string>("""
            SELECT concat_ws(',', rf0, rf1, rf2, rf3) AS "Value" FROM planet_members WHERE id = 20
            """).SingleAsync());
        Assert.Equal("1,2,4,8", await db.Database.SqlQueryRaw<string>("""
            SELECT concat_ws(',', rf0, rf1, rf2, rf3) AS "Value" FROM planet_members WHERE id = 30
            """).SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value" FROM planets WHERE version < 1
            """).SingleAsync());
        Assert.False(await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_class WHERE relnamespace = pg_my_temp_schema()
                AND relname = 'planet_role_members') AS "Value"
            """).SingleAsync());
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Drop_RejectsLegacyPlanetWithTooManyRoles()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await CreateLegacySchema(db);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO planets VALUES (1, 0);
            INSERT INTO planet_roles SELECT i, 1, i = 0, i, 0 FROM generate_series(0, 256) i;
            INSERT INTO planet_members VALUES (10, 1, 0, 0, 0, 0);
            INSERT INTO planet_role_members VALUES (10, 0);
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => ApplyMigration(db));
        Assert.Contains("more than 256 roles", error.MessageText);
        await transaction.RollbackAsync();
    }
}
