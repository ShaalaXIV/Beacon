using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beacon.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExactProfileAgeAndRemovePronouns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pronouns",
                table: "Profiles");

            migrationBuilder.AlterColumn<int>(
                name: "Age",
                table: "Profiles",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Age",
                table: "Profiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pronouns",
                table: "Profiles",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }
    }
}
