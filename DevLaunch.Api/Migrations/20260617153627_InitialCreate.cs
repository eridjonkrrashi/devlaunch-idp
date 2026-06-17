using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevLaunch.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Namespace = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CpuQuota = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MemoryQuota = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaxApps = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Namespace = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Replicas = table.Column<int>(type: "integer", nullable: false),
                    EnvVars = table.Column<string>(type: "text", nullable: false),
                    CpuRequest = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CpuLimit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MemoryRequest = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MemoryLimit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IngressHost = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CurrentRevision = table.Column<int>(type: "integer", nullable: false),
                    RolloutPhase = table.Column<string>(type: "text", nullable: false),
                    RolloutStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RolloutMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    HpaEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HpaMinReplicas = table.Column<int>(type: "integer", nullable: false),
                    HpaMaxReplicas = table.Column<int>(type: "integer", nullable: false),
                    HpaCpuTargetPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "DeploymentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Replicas = table.Column<int>(type: "integer", nullable: false),
                    SpecSnapshot = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentRevisions_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_Applications_ProjectId_Name",
                table: "Applications",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ProjectId",
                table: "AuditEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Timestamp",
                table: "AuditEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRevisions_ApplicationId_RevisionNumber",
                table: "DeploymentRevisions",
                columns: new[] { "ApplicationId", "RevisionNumber" },
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "DeploymentRevisions");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
