using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevLaunch.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Namespace = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Image = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Replicas = table.Column<int>(type: "INTEGER", nullable: false),
                    EnvVars = table.Column<string>(type: "text", nullable: false),
                    CpuRequest = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CpuLimit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MemoryRequest = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MemoryLimit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IngressHost = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Image = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Replicas = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRevisions_ApplicationId_RevisionNumber",
                table: "DeploymentRevisions",
                columns: new[] { "ApplicationId", "RevisionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentRevisions");

            migrationBuilder.DropTable(
                name: "Applications");
        }
    }
}
