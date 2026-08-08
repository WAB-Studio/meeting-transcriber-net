using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeetingTranscriber.Infrastructure.Migrations
{
    /// <summary>
    /// Gives the two columns that hold when something was settled the names that say so:
    /// <c>artifacts.confirmed_at</c> and <c>speaker_assignments.assigned_at</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were called <c>created_at</c> and both were rewritten in place — an artifact whenever a
    /// derivative was rendered again, an assignment whenever somebody corrected what the channel
    /// had guessed. The value was the right one to keep; the name promised the row's own age, so
    /// anything reading it to ask how old a corpus was would have been answered with when it was
    /// last touched. <c>assigned_at</c> also finishes a pair that was written half way: the row
    /// already carried <c>assigned_by</c>.
    /// </para>
    /// <para>
    /// From here <c>created_at</c> means what it says everywhere, and the model enforces it —
    /// every column of that name is read-only once its row exists, so the next timestamp that
    /// wants to move has to be named for what it records.
    /// </para>
    /// </remarks>
    public partial class ConfirmedAndAssigned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "speaker_assignments",
                newName: "assigned_at");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "artifacts",
                newName: "confirmed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "assigned_at",
                table: "speaker_assignments",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "confirmed_at",
                table: "artifacts",
                newName: "created_at");
        }
    }
}
