using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevLaunch.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectsAuthAndHpa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Applications_Name",
                table: "Applications");

            migrationBuilder.AddColumn<int>(
                name: "HpaCpuTargetPercent",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HpaEnabled",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HpaMaxReplicas",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HpaMinReplicas",
                table: "Applications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "RolloutMessage",
                table: "Applications",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RolloutPhase",
                table: "Applications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RolloutStartedAt",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Namespace = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CpuQuota = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MemoryQuota = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MaxApps = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_ProjectId_Name",
                table: "Applications",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_ProjectId",
                table: "ApiKeys",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ProjectId",
                table: "AuditEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Timestamp",
                table: "AuditEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Namespace",
                table: "Projects",
                column: "Namespace",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Projects_ProjectId",
                table: "Applications",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_Projects_ProjectId",
                table: "Applications");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Applications_ProjectId_Name",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "HpaCpuTargetPercent",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "HpaEnabled",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "HpaMaxReplicas",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "HpaMinReplicas",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RolloutMessage",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RolloutPhase",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "RolloutStartedAt",
                table: "Applications");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name",
                unique: true);
        }
    }
}
