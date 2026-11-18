using System.Data.Common;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    public class LinkRepository : RepositoryBase, ILinkRepository
    {
        private const string SelectColumns = $@"
            link.id,
            link.shelf_id AS {nameof(LinkEntity.ShelfId)},
            link.kind,
            link.file_id AS {nameof(LinkEntity.FileId)},
            link.path,
            link.overwrite,
            link.description,
            link.created_by_user_id AS {nameof(LinkEntity.CreatedByUserId)},
            link.created_by_key_id AS {nameof(LinkEntity.CreatedByKeyId)},
            link.created_at AS {nameof(LinkEntity.CreatedAt)},
            link.expires_at AS {nameof(LinkEntity.ExpiresAt)}";

        // Matches every row when both are null, else the rows of the one maker given: a row names exactly
        // one. Unqualified, for the DELETE too; no table it is joined with has these columns.
        private const string MadeByFilter = $@"
            (@{nameof(MakerFilter.MakerUserId)} IS NULL OR created_by_user_id = @{nameof(MakerFilter.MakerUserId)})
            AND (@{nameof(MakerFilter.MakerKeyId)} IS NULL OR created_by_key_id = @{nameof(MakerFilter.MakerKeyId)})";

        private const string Download = "download";
        private const string Upload = "upload";

        public LinkRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task InsertAsync(LinkRecord link, IDbSession? session = null)
        {
            await UseSessionAsync(async (dbSession) =>
            {
                string query = @"
                    INSERT INTO shelf_link (id, shelf_id, kind, file_id, path, overwrite, description, created_by_user_id, created_by_key_id, created_at, expires_at)
                    VALUES (@Id, @ShelfId, @Kind, @FileId, @Path, @Overwrite, @Description, @CreatedByUserId, @CreatedByKeyId, @CreatedAt, @ExpiresAt)";

                return await dbSession.Connection.ExecuteAsync(query, LinkEntity.From(link), transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<LinkRecord?> GetLiveAsync(Guid id, DateTimeOffset now, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT {SelectColumns}
                    FROM shelf_link link
                    WHERE link.id = @{nameof(id)} AND link.expires_at > @{nameof(now)}";

                LinkEntity? entity = await dbSession.Connection.QuerySingleOrDefaultAsync<LinkEntity>(
                    query, new { id, now }, transaction: dbSession.Transaction);

                return entity?.ToDomain();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ListedLinkRecord>> GetLiveForShelfAsync(Guid shelfId, ShelfCallerReference? madeBy, DateTimeOffset now, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                // file_path walks up from each download link's live file, a directory a step, prefixing
                // each name; the row that has reached the shelf's root holds the whole path. One query
                // for the whole list, however many links it holds.
                string query = $@"
                    WITH RECURSIVE file_path (link_id, directory_id, path) AS (
                        SELECT link.id, f.directory_id, '/' || f.name
                        FROM shelf_link link
                        JOIN file f ON f.id = link.file_id AND f.deleted_at IS NULL
                        WHERE link.shelf_id = @{nameof(shelfId)} AND link.expires_at > @{nameof(now)} AND {MadeByFilter}
                        UNION ALL
                        SELECT file_path.link_id, d.parent_id, '/' || d.name || file_path.path
                        FROM file_path
                        JOIN directory d ON d.id = file_path.directory_id
                        WHERE d.parent_id IS NOT NULL
                    )
                    SELECT {SelectColumns},
                        COALESCE(maker_user.display_name, maker_key.name) AS {nameof(LinkEntity.MakerName)},
                        live_file.path AS {nameof(LinkEntity.LiveFilePath)}
                    FROM shelf_link link
                    LEFT JOIN user_account maker_user ON maker_user.id = link.created_by_user_id
                    LEFT JOIN shelf_key maker_key ON maker_key.id = link.created_by_key_id
                    LEFT JOIN file_path live_file ON live_file.link_id = link.id
                        AND live_file.directory_id = (SELECT id FROM directory WHERE shelf_id = @{nameof(shelfId)} AND parent_id IS NULL)
                    WHERE link.shelf_id = @{nameof(shelfId)} AND link.expires_at > @{nameof(now)} AND {MadeByFilter}
                    ORDER BY link.created_at DESC, link.id";

                MakerFilter filter = MakerFilter.For(madeBy);
                IEnumerable<LinkEntity> entities = await dbSession.Connection.QueryAsync<LinkEntity>(
                    query, new { shelfId, now, filter.MakerUserId, filter.MakerKeyId }, transaction: dbSession.Transaction);

                return (IReadOnlyList<ListedLinkRecord>)entities
                    .Select(entity => new ListedLinkRecord(entity.ToDomain(), entity.MakerName, entity.LiveFilePath))
                    .ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(Guid shelfId, Guid id, ShelfCallerReference? madeBy, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    DELETE FROM shelf_link
                    WHERE id = @{nameof(id)} AND shelf_id = @{nameof(shelfId)} AND {MadeByFilter}";

                MakerFilter filter = MakerFilter.For(madeBy);
                return await dbSession.Connection.ExecuteAsync(
                    query, new { id, shelfId, filter.MakerUserId, filter.MakerKeyId }, transaction: dbSession.Transaction) > 0;
            }, session);
        }

        /// <inheritdoc />
        public async Task<int> DeleteExpiredAsync(DateTimeOffset now, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                return await dbSession.Connection.ExecuteAsync(
                    $"DELETE FROM shelf_link WHERE expires_at <= @{nameof(now)}", new { now }, transaction: dbSession.Transaction);
            }, session);
        }

        private static string ToColumn(ShelfLinkKind kind)
        {
            return kind switch
            {
                ShelfLinkKind.Download => Download,
                ShelfLinkKind.Upload => Upload,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a link kind."),
            };
        }

        /// <summary><see cref="MadeByFilter"/>'s parameters: one maker's id in its column, or neither for every maker.</summary>
        private sealed record MakerFilter(Guid? MakerUserId, Guid? MakerKeyId)
        {
            public static MakerFilter For(ShelfCallerReference? madeBy)
            {
                return new MakerFilter(
                    madeBy is { Kind: ShelfCallerKind.User } ? madeBy.Id : null,
                    madeBy is { Kind: ShelfCallerKind.Key } ? madeBy.Id : null);
            }
        }

        internal class LinkEntity
        {
            public Guid Id { get; set; }
            public Guid ShelfId { get; set; }
            public string Kind { get; set; } = string.Empty;
            public Guid? FileId { get; set; }
            public string? Path { get; set; }
            public bool? Overwrite { get; set; }
            public string? Description { get; set; }
            public Guid? CreatedByUserId { get; set; }
            public Guid? CreatedByKeyId { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public string MakerName { get; set; } = string.Empty;
            public string? LiveFilePath { get; set; }

            public static LinkEntity From(LinkRecord link)
            {
                LinkEntity entity = new LinkEntity
                {
                    Id = link.Id,
                    ShelfId = link.ShelfId,
                    Kind = ToColumn(link.Target.Kind),
                    CreatedByUserId = link.CreatedBy.Kind == ShelfCallerKind.User ? link.CreatedBy.Id : null,
                    CreatedByKeyId = link.CreatedBy.Kind == ShelfCallerKind.Key ? link.CreatedBy.Id : null,
                    CreatedAt = link.CreatedAt,
                    ExpiresAt = link.ExpiresAt,
                };

                switch (link.Target)
                {
                    case DownloadLinkTarget download:
                        entity.FileId = download.FileId;
                        break;
                    case UploadLinkTarget upload:
                        entity.Path = upload.Path;
                        entity.Overwrite = upload.Overwrite;
                        entity.Description = upload.Description;
                        break;
                }

                return entity;
            }

            public LinkRecord ToDomain()
            {
                LinkTarget target = Kind switch
                {
                    Download => new DownloadLinkTarget(FileId!.Value),
                    Upload => new UploadLinkTarget(Path!, Overwrite!.Value, Description),
                    _ => throw new InvalidOperationException($"'{Kind}' is not a stored link kind."),
                };

                ShelfCallerReference createdBy = CreatedByUserId is Guid userId
                    ? new ShelfCallerReference(ShelfCallerKind.User, userId)
                    : new ShelfCallerReference(ShelfCallerKind.Key, CreatedByKeyId!.Value);

                return new LinkRecord
                {
                    Id = Id,
                    ShelfId = ShelfId,
                    Target = target,
                    CreatedBy = createdBy,
                    CreatedAt = CreatedAt,
                    ExpiresAt = ExpiresAt,
                };
            }
        }
    }
}
