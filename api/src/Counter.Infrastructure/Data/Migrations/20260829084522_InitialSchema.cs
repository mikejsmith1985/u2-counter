using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Counter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAt = table.Column<string>(type: "TEXT", nullable: false),
                    UserSubject = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DatabaseLogin = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DatabaseLoginIsShared = table.Column<bool>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSession",
                columns: table => new
                {
                    SessionKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserSubject = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BranchCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    SelectedCustomerAccount = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSession", x => x.SessionKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityRecord_UserSubject_OccurredAt",
                table: "ActivityRecord",
                columns: new[] { "UserSubject", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_ExpiresAt",
                table: "UserSession",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityRecord");

            migrationBuilder.DropTable(
                name: "UserSession");
        }
    }
}
