using ByteShelfCommon;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>
    /// The one walk of a directory's live subtree, shared by search and tree listings: <see cref="Sql"/>
    /// is a <c>WITH RECURSIVE tree(node_id, node_path, level)</c> holding the directory (level 0) and
    /// every live directory under it down to <see cref="MaxLevel"/>, each with its full path built as it
    /// is walked, so what is found can be ordered and paged by path. A query that uses it passes these
    /// properties among its parameters, under their own names.
    /// </summary>
    /// <remarks>
    /// Each page walks the whole subtree again, so listing a tree of N entries page by page costs about
    /// N² / page size rows. Search pays the same; paging an ordered walk instead is left until a shelf
    /// large enough to feel it.
    /// </remarks>
    internal sealed record SubtreeWalk(Guid DirectoryId, string BasePath, int? MaxLevel)
    {
        public const string Sql = $@"
            WITH RECURSIVE tree(node_id, node_path, level) AS (
                SELECT @{nameof(DirectoryId)}, @{nameof(BasePath)}, 0
                UNION ALL
                SELECT directory.id, tree.node_path || '/' || directory.name, tree.level + 1
                FROM directory JOIN tree ON directory.parent_id = tree.node_id
                -- A trashed directory holds only trashed entries, so this only spares walking it.
                WHERE directory.deleted_at IS NULL
                  AND (@{nameof(MaxLevel)} IS NULL OR tree.level < @{nameof(MaxLevel)})
            )";

        /// <summary>The walk whose directories are the ones down to <paramref name="depth"/> levels below the directory (null: all).</summary>
        public static SubtreeWalk ForDirectories(Guid directoryId, string directoryPath, int? depth)
        {
            return new SubtreeWalk(directoryId, BasePathOf(directoryPath), depth);
        }

        /// <summary>
        /// The walk whose directories hold the files down to <paramref name="depth"/> levels below the
        /// directory (null: all): a file is one level below the directory it is in.
        /// </summary>
        public static SubtreeWalk ForFiles(Guid directoryId, string directoryPath, int? depth)
        {
            return new SubtreeWalk(directoryId, BasePathOf(directoryPath), depth - 1);
        }

        /// <summary>The walk's parameters, under the names <see cref="Sql"/> uses.</summary>
        public DynamicParameters Parameters()
        {
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add(nameof(DirectoryId), DirectoryId);
            parameters.Add(nameof(BasePath), BasePath);
            parameters.Add(nameof(MaxLevel), MaxLevel);
            return parameters;
        }

        /// <summary>The root's own path is empty here, so its children's are "/name".</summary>
        internal static string BasePathOf(string directoryPath)
        {
            return directoryPath == ShelfPath.Root ? string.Empty : directoryPath;
        }
    }

    /// <summary>
    /// The walk of what one deletion removed, shared by its directories' and files' listings:
    /// <see cref="Sql"/> is a <c>WITH RECURSIVE tree(node_id, node_path, level)</c> holding the directory
    /// the deletion was made in (level 0) and every directory the deletion removed under it, each with
    /// the path it had. A query that uses it passes these properties among its parameters, and keeps
    /// only what <see cref="Keeps"/> says. It sits beside <see cref="SubtreeWalk"/>, its pair, as small
    /// related types share a file here.
    /// </summary>
    internal sealed record DeletionWalk(Guid DirectoryId, string BasePath, Guid DeletionId, string? Under)
    {
        public const string Sql = $@"
            WITH RECURSIVE tree(node_id, node_path, level) AS (
                SELECT @{nameof(DirectoryId)}, @{nameof(BasePath)}, 0
                UNION ALL
                SELECT directory.id, tree.node_path || '/' || directory.name, tree.level + 1
                FROM directory JOIN tree ON directory.parent_id = tree.node_id
                WHERE directory.deletion_id = @{nameof(DeletionId)}
            )";

        /// <param name="containerId">The directory the deletion was made in.</param>
        /// <param name="containerPath">That directory's path.</param>
        /// <param name="under">A path the deletion took, to keep only what is at it and under it; null keeps all.</param>
        public static DeletionWalk From(Guid containerId, string containerPath, Guid deletionId, string? under)
        {
            return new DeletionWalk(containerId, SubtreeWalk.BasePathOf(containerPath), deletionId, under);
        }

        /// <summary>The walk's parameters, under the names <see cref="Sql"/> and <see cref="Keeps"/> use.</summary>
        public DynamicParameters Parameters()
        {
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add(nameof(DirectoryId), DirectoryId);
            parameters.Add(nameof(BasePath), BasePath);
            parameters.Add(nameof(DeletionId), DeletionId);
            parameters.Add(nameof(Under), Under);
            return parameters;
        }

        /// <summary>
        /// The condition that keeps an entry whose path is <paramref name="pathColumn"/>: at
        /// <see cref="Under"/> or below it. Compared by substring, as a path may hold LIKE's wildcards.
        /// </summary>
        public static string Keeps(string pathColumn)
        {
            const string under = $"@{nameof(Under)}";
            return $"({under} IS NULL OR {pathColumn} = {under} OR substr({pathColumn}, 1, length({under}) + 1) = {under} || '/')";
        }
    }
}
