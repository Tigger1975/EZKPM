using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EZKPM.Server.PDP.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Block UPDATE on AuditLogs
            migrationBuilder.Sql(@"
                CREATE TRIGGER PreventAuditLogUpdate 
                BEFORE UPDATE ON AuditLogs 
                BEGIN 
                    SELECT RAISE(ABORT, 'Compliance Violation: WORM (Write Once Read Many) policy enforces immutability. Updates are not allowed.'); 
                END;");

            // Block DELETE on AuditLogs
            migrationBuilder.Sql(@"
                CREATE TRIGGER PreventAuditLogDelete 
                BEFORE DELETE ON AuditLogs 
                BEGIN 
                    SELECT RAISE(ABORT, 'Compliance Violation: WORM (Write Once Read Many) policy enforces immutability. Deletions are not allowed.'); 
                END;");

            // Block UPDATE on RecoveryAuditLogs
            migrationBuilder.Sql(@"
                CREATE TRIGGER PreventRecoveryAuditLogUpdate 
                BEFORE UPDATE ON RecoveryAuditLogs 
                BEGIN 
                    SELECT RAISE(ABORT, 'Compliance Violation: WORM policy enforces immutability. Updates are not allowed.'); 
                END;");

            // Block DELETE on RecoveryAuditLogs
            migrationBuilder.Sql(@"
                CREATE TRIGGER PreventRecoveryAuditLogDelete 
                BEFORE DELETE ON RecoveryAuditLogs 
                BEGIN 
                    SELECT RAISE(ABORT, 'Compliance Violation: WORM policy enforces immutability. Deletions are not allowed.'); 
                END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS PreventAuditLogUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS PreventAuditLogDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS PreventRecoveryAuditLogUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS PreventRecoveryAuditLogDelete;");
        }
    }
}
