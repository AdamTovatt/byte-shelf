namespace ByteShelf.Server.ChangeLog.Models
{
    /// <summary>
    /// What an entity type's rows go with: the database removes a row with its owner's, by a foreign key, and
    /// nothing logs that. So a server that keeps the owner longer, as a replica keeps a purged file in its
    /// trash, keeps the row too.
    /// </summary>
    /// <param name="EntityType">The owner's type.</param>
    /// <param name="OwnedTable">The table the owned rows are in.</param>
    /// <param name="OwnedColumn">The column of it that names a row's owner.</param>
    public sealed record EntityOwner(string EntityType, string OwnedTable, string OwnedColumn);
}
