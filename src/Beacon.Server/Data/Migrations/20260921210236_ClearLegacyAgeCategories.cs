using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearLegacyAgeCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old values were enum ordinals (YoungAdult = 1, Adult = 2, and so on), not years.
            // Clear them after the preceding migration makes Age nullable.
            migrationBuilder.Sql("UPDATE \"Profiles\" SET \"Age\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Exact ages cannot be mapped back to the removed categories without inventing data.
        }
    }
}
