namespace Tsabo.Data.Schema;

public enum MigrationOperationType
{
    CreateTable,
    AddColumn,
    CreateIndex,
    DropTable,
    DropColumn,
    DropIndex,
    ModifyColumn,
    AddForeignKey,
    DropForeignKey,
}
