using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sparta.Modules.Inventory;

#nullable disable

namespace Sparta.Api.Migrations.Inventory;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260922090000_EnableProductChangeTracking")]
public sealed class EnableProductChangeTracking : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.change_tracking_databases WHERE database_id = DB_ID()
            )
            BEGIN
                DECLARE @enableDatabase nvarchar(max) =
                    N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) +
                    N' SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 14 DAYS, AUTO_CLEANUP = ON);';
                EXEC sp_executesql @enableDatabase;
            END;
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.Products')
            )
            BEGIN
                ALTER TABLE dbo.Products ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.Products')
            )
            BEGIN
                ALTER TABLE dbo.Products DISABLE CHANGE_TRACKING;
            END;
            """);

        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM sys.change_tracking_databases WHERE database_id = DB_ID()
            )
            AND NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables)
            BEGIN
                DECLARE @disableDatabase nvarchar(max) =
                    N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET CHANGE_TRACKING = OFF;';
                EXEC sp_executesql @disableDatabase;
            END;
            """, suppressTransaction: true);
    }
}
