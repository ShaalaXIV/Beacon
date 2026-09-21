using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compass.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialProduction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IsModerator = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBanned = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdultConfirmedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeaconId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedByAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeaconId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    LitAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LitUntil = table.Column<long>(type: "INTEGER", nullable: false),
                    ExtinguishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Reason = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeaconId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReporterAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Resolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ResolvedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Beacons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    TerritoryId = table.Column<ushort>(type: "INTEGER", nullable: false),
                    MapId = table.Column<uint>(type: "INTEGER", nullable: false),
                    ZoneName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SubAreaName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    X = table.Column<float>(type: "REAL", nullable: false),
                    Y = table.Column<float>(type: "REAL", nullable: false),
                    Z = table.Column<float>(type: "REAL", nullable: false),
                    MapX = table.Column<float>(type: "REAL", nullable: false),
                    MapY = table.Column<float>(type: "REAL", nullable: false),
                    NearestAetheryteId = table.Column<uint>(type: "INTEGER", nullable: false),
                    NearestAetheryteName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AethernetShard = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WorldId = table.Column<uint>(type: "INTEGER", nullable: false),
                    WorldName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    DataCenter = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsLit = table.Column<bool>(type: "INTEGER", nullable: false),
                    LitAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LitUntil = table.Column<long>(type: "INTEGER", nullable: true),
                    LitByName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    LitByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LitNote = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AllowPublicLighting = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastLitAt = table.Column<long>(type: "INTEGER", nullable: true),
                    TimesLit = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TagsCsv = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Visibility = table.Column<int>(type: "INTEGER", nullable: false),
                    ShareCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FavoriteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beacons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Beacons_Accounts_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    WorldId = table.Column<uint>(type: "INTEGER", nullable: false),
                    WorldName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    DataCenter = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    LinkedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    WorldId = table.Column<uint>(type: "INTEGER", nullable: false),
                    WorldName = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    DataCenter = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Race = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Clan = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Age = table.Column<int>(type: "INTEGER", nullable: false),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Pronouns = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ArchetypeCsv = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Quote = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Length = table.Column<int>(type: "INTEGER", nullable: false),
                    Boundaries = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    WalkupsWelcome = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsMature = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayerTimezone = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    PlayerAvailability = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PlayerContact = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    Overview = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    History = table.Column<string>(type: "TEXT", maxLength: 4400, nullable: true),
                    Goals = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PortraitImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Visibility = table.Column<int>(type: "INTEGER", nullable: false),
                    Availability = table.Column<int>(type: "INTEGER", nullable: false),
                    ShareCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    LastActiveAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Accounts_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Favorites",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BeaconId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favorites", x => new { x.AccountId, x.BeaconId });
                    table.ForeignKey(
                        name: "FK_Favorites_Beacons_BeaconId",
                        column: x => x.BeaconId,
                        principalTable: "Beacons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileHooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileHooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileHooks_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileImages_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OtherProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    ConfirmedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileLinks_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileTags_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_KeyHash",
                table: "Accounts",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_DataCenter",
                table: "Beacons",
                column: "DataCenter");

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_IsDeleted_Visibility_IsLit",
                table: "Beacons",
                columns: new[] { "IsDeleted", "Visibility", "IsLit" });

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_IsLit_LitUntil",
                table: "Beacons",
                columns: new[] { "IsLit", "LitUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_OwnerAccountId",
                table: "Beacons",
                column: "OwnerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_ShareCode",
                table: "Beacons",
                column: "ShareCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_TerritoryId",
                table: "Beacons",
                column: "TerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Beacons_WorldId",
                table: "Beacons",
                column: "WorldId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_AccountId",
                table: "Characters",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Name_WorldId",
                table: "Characters",
                columns: new[] { "Name", "WorldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_BeaconId",
                table: "Favorites",
                column: "BeaconId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_BeaconId",
                table: "Images",
                column: "BeaconId");

            migrationBuilder.CreateIndex(
                name: "IX_Lights_AccountId",
                table: "Lights",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Lights_BeaconId",
                table: "Lights",
                column: "BeaconId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileHooks_ProfileId",
                table: "ProfileHooks",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileImages_ImageId",
                table: "ProfileImages",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileImages_ProfileId",
                table: "ProfileImages",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileLinks_OtherProfileId",
                table: "ProfileLinks",
                column: "OtherProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileLinks_ProfileId",
                table: "ProfileLinks",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileLinks_ProfileId_OtherProfileId",
                table: "ProfileLinks",
                columns: new[] { "ProfileId", "OtherProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileTags_Kind_Value",
                table: "ProfileTags",
                columns: new[] { "Kind", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileTags_ProfileId",
                table: "ProfileTags",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileTags_ProfileId_Kind_Value",
                table: "ProfileTags",
                columns: new[] { "ProfileId", "Kind", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_CharacterName_WorldId",
                table: "Profiles",
                columns: new[] { "CharacterName", "WorldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_DataCenter",
                table: "Profiles",
                column: "DataCenter");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_IsDeleted_Visibility_LastActiveAt",
                table: "Profiles",
                columns: new[] { "IsDeleted", "Visibility", "LastActiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_OwnerAccountId",
                table: "Profiles",
                column: "OwnerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_Race",
                table: "Profiles",
                column: "Race");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_ShareCode",
                table: "Profiles",
                column: "ShareCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reports_BeaconId",
                table: "Reports",
                column: "BeaconId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ProfileId",
                table: "Reports",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Resolved_CreatedAt",
                table: "Reports",
                columns: new[] { "Resolved", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "Favorites");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "Lights");

            migrationBuilder.DropTable(
                name: "ProfileHooks");

            migrationBuilder.DropTable(
                name: "ProfileImages");

            migrationBuilder.DropTable(
                name: "ProfileLinks");

            migrationBuilder.DropTable(
                name: "ProfileTags");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "Beacons");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
