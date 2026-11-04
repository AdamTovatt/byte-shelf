using ByteShelfClient;
using ByteShelfClient.Encryption;
using ByteShelfCommon;

namespace ByteShelf.Tools
{
    /// <summary>
    /// What a password is asked for: a new one, to be typed twice, or one to check against what it opens.
    /// When it is an encrypted folder's password, it carries the folder's path, so the person asked is
    /// told which password is wanted.
    /// </summary>
    internal sealed class PasswordUse
    {
        public static readonly PasswordUse New = new PasswordUse(isNew: true, folderPath: null);

        public static readonly PasswordUse Existing = new PasswordUse(isNew: false, folderPath: null);

        private PasswordUse(bool isNew, string? folderPath)
        {
            IsNew = isNew;
            FolderPath = folderPath;
        }

        public bool IsNew { get; }

        /// <summary>The path of the encrypted folder whose password it is, when it is one's.</summary>
        public string? FolderPath { get; }

        public static PasswordUse Of(EncryptedFolder folder)
        {
            return new PasswordUse(isNew: false, folder.Directory.Path);
        }
    }

    /// <summary>
    /// Encrypting and decrypting what is stored, in place: a client downloads the file, encrypts or
    /// decrypts it, and swaps its contents for the result, so it keeps its id and the old contents are
    /// gone at once, never to the trash (<see cref="IShelfClient.SwapContentAsync"/>). A directory is encrypted first, so nothing plain lands
    /// in it meanwhile, and then every file under it; decrypting removes the directory's encryption
    /// first, as the server keeps plain files out of an encrypted one. Either can be run again to
    /// finish one that was stopped part way: encrypting goes on under the encrypted directory, and
    /// decrypting a directory that is no longer encrypted decrypts what is still encrypted under it.
    /// </summary>
    internal static class Encrypting
    {
        /// <summary>
        /// Encrypts the file at <paramref name="location"/>, or the directory with every file under it.
        /// A file outside encrypted directories takes a new password; anything else, the directory's.
        /// </summary>
        /// <param name="password">Asked once it is known which kind of password is needed.</param>
        /// <param name="workFactor">The scrypt work factor to encrypt with.</param>
        /// <exception cref="UsageException">
        /// It is inside an encrypted directory it would not be encrypted with, the password is not its
        /// directory's, or a directory to encrypt holds a file encrypted with another password.
        /// </exception>
        public static async Task<FolderTransferResult> EncryptAsync(IShelfClient shelf, ShelfLocation location, Func<PasswordUse, string> password, int workFactor, CancellationToken cancellationToken)
        {
            Entry entry = await Entries.GetAsync(shelf, location.Path, cancellationToken);
            EncryptedFolder? folder = (await shelf.GetEncryptionAsync(location.Path, cancellationToken)).EncryptedBy;
            FolderTransferResult result = new FolderTransferResult($"{location.Shelf}:{location.Path}");

            if (entry.File is ShelfFile file)
            {
                if (!file.Encrypted)
                {
                    string chosen = await PasswordForAsync(folder, PasswordUse.New, password, cancellationToken);
                    await FolderTransfers.StopOnAsync(result, cancellationToken, () => EncryptFileAsync(shelf, file, new Encryptor(chosen, workFactor), result, cancellationToken));
                }

                return result;
            }

            string directoryPath = entry.Directory!.Path;
            if (folder != null && folder.Directory.Path != directoryPath)
            {
                throw new UsageException($"'{directoryPath}' is inside the encrypted folder '{folder.Directory.Path}'; encrypt that folder to finish what is under it.");
            }

            Encryptor encryptor = new Encryptor(await PasswordForAsync(folder, PasswordUse.New, password, cancellationToken), workFactor);
            List<ShelfFile> files = await FilesUnderAsync(shelf, directoryPath, cancellationToken);
            HashSet<Guid> checkedFiles = new HashSet<Guid>();
            if (folder == null)
            {
                // Refused before anything changes: once the directory is encrypted, a file under it
                // could no longer be decrypted with its own password.
                List<ShelfFile> encrypted = files.Where(under => under.Encrypted).ToList();
                await RequireAllOpenedByAsync(shelf, encrypted, encryptor.Password, cancellationToken);
                checkedFiles.UnionWith(encrypted.Select(under => under.Id));
            }

            await FolderTransfers.StopOnAsync(result, cancellationToken, async () =>
            {
                if (folder == null)
                {
                    await shelf.EncryptDirectoryAsync(directoryPath, await encryptor.CreateCheckAsync(cancellationToken), cancellationToken);

                    // Listed again now that nothing plain can land in it: a file stored after the first
                    // listing would be left plain otherwise.
                    files = await FilesUnderAsync(shelf, directoryPath, cancellationToken);
                }

                foreach (ShelfFile under in files)
                {
                    await FolderTransfers.RunAsync(under.Path, result, async () =>
                    {
                        if (!under.Encrypted)
                        {
                            await EncryptFileAsync(shelf, under, encryptor, result, cancellationToken);
                        }
                        else if (!checkedFiles.Contains(under.Id) && !await IsOpenedByAsync(shelf, under, encryptor.Password, cancellationToken))
                        {
                            // One scrypt per encrypted file when run again, slow on a large folder, and kept:
                            // a file under the folder that its password does not open is told now, not
                            // when someone tries to open it.
                            // Only a client that skipped the folder's check could have stored it here.
                            throw new UsageException("it is encrypted with another password; move it out of this folder to decrypt it with that one.");
                        }
                    });
                }
            });

            return result;
        }

        /// <summary>
        /// Decrypts the file at <paramref name="location"/>, or stops encrypting the directory and decrypts
        /// every file under it. A directory no longer encrypted (a decryption that stopped part way) has
        /// what is still encrypted under it decrypted with the password given.
        /// </summary>
        /// <exception cref="UsageException">It is under an encrypted directory it is not itself, the password is not its directory's, or an encrypted directory is under it.</exception>
        public static async Task<FolderTransferResult> DecryptAsync(IShelfClient shelf, ShelfLocation location, Func<PasswordUse, string> password, CancellationToken cancellationToken)
        {
            Entry entry = await Entries.GetAsync(shelf, location.Path, cancellationToken);
            EncryptedFolder? folder = (await shelf.GetEncryptionAsync(location.Path, cancellationToken)).EncryptedBy;
            FolderTransferResult result = new FolderTransferResult($"{location.Shelf}:{location.Path}");

            if (entry.File is ShelfFile file)
            {
                if (folder != null)
                {
                    throw new UsageException($"'{file.Path}' is under the encrypted folder '{folder.Directory.Path}'; remove that folder's encryption instead.");
                }

                if (file.Encrypted)
                {
                    string given = password(PasswordUse.Existing);
                    await FolderTransfers.StopOnAsync(result, cancellationToken, () => DecryptFileAsync(shelf, file, given, result, cancellationToken));
                }

                return result;
            }

            string directoryPath = entry.Directory!.Path;
            if (folder != null && folder.Directory.Path != directoryPath)
            {
                throw new UsageException($"'{directoryPath}' is inside the encrypted folder '{folder.Directory.Path}'; remove that folder's encryption instead.");
            }

            (List<ShelfFile> files, ShelfDirectory? encryptedBelow) = await TreeUnderAsync(shelf, directoryPath, cancellationToken);
            if (encryptedBelow != null)
            {
                throw new UsageException($"The encrypted folder '{encryptedBelow.Path}' is under '{directoryPath}'; decrypt that folder itself.");
            }

            string folderPassword = await PasswordForAsync(folder, PasswordUse.Existing, password, cancellationToken);
            await FolderTransfers.StopOnAsync(result, cancellationToken, async () =>
            {
                if (folder != null)
                {
                    await shelf.RemoveDirectoryEncryptionAsync(directoryPath, cancellationToken);
                }

                foreach (ShelfFile under in files.Where(under => under.Encrypted))
                {
                    await FolderTransfers.RunAsync(under.Path, result, () => DecryptFileAsync(shelf, under, folderPassword, result, cancellationToken));
                }
            });

            return result;
        }

        /// <summary>The password of <paramref name="folder"/>, asked for and checked against its check.</summary>
        /// <exception cref="UsageException">It is not the folder's password.</exception>
        public static async Task<string> FolderPasswordAsync(EncryptedFolder folder, Func<PasswordUse, string> password, CancellationToken cancellationToken)
        {
            string given = password(PasswordUse.Of(folder));
            if (!await FolderPassword.MatchesAsync(folder, given, cancellationToken))
            {
                throw new UsageException($"That is not the password of the encrypted folder '{folder.Directory.Path}'.");
            }

            return given;
        }

        /// <summary>
        /// A password to decrypt what is at <paramref name="path"/> with: checked against its encrypted
        /// folder's check when it is under one, so a wrong one is told at once rather than by every file.
        /// </summary>
        public static async Task<string> ExistingPasswordAsync(IShelfClient shelf, string path, Func<PasswordUse, string> password, CancellationToken cancellationToken)
        {
            EncryptedFolder? folder = (await shelf.GetEncryptionAsync(path, cancellationToken)).EncryptedBy;
            return await PasswordForAsync(folder, PasswordUse.Existing, password, cancellationToken);
        }

        /// <summary>
        /// The password for a place: <paramref name="folder"/>'s, checked, when the place is under one;
        /// else one asked for as <paramref name="outsideFolders"/> says.
        /// </summary>
        /// <exception cref="UsageException">It is not the folder's password.</exception>
        public static async Task<string> PasswordForAsync(EncryptedFolder? folder, PasswordUse outsideFolders, Func<PasswordUse, string> password, CancellationToken cancellationToken)
        {
            return folder != null ? await FolderPasswordAsync(folder, password, cancellationToken) : password(outsideFolders);
        }

        /// <summary>
        /// Swaps the file's contents for themselves encrypted, keeping its id, type and description; the
        /// plain ones are gone at once. Refused when the file changed since it was listed.
        /// </summary>
        private static async Task EncryptFileAsync(IShelfClient shelf, ShelfFile file, Encryptor encryptor, FolderTransferResult result, CancellationToken cancellationToken)
        {
            await using FileStream encrypted = Transfers.OpenSideFile();
            await using (Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken))
            {
                await encryptor.EncryptAsync(content, encrypted, cancellationToken);
            }

            encrypted.Position = 0;
            await shelf.SwapContentAsync(file, encrypted, encrypted: true, cancellationToken);
            result.Add(file.SizeBytes);
        }

        /// <summary>
        /// Swaps the file's contents for themselves decrypted, keeping its id, type and description; the
        /// encrypted ones are gone at once. Refused when the file changed since it was listed.
        /// </summary>
        private static async Task DecryptFileAsync(IShelfClient shelf, ShelfFile file, string password, FolderTransferResult result, CancellationToken cancellationToken)
        {
            await using FileStream decrypted = Transfers.OpenSideFile();
            await Transfers.WriteAsync(FileSource.Live(shelf), file, decrypted, EncryptedFiles.DecryptedWith(password), cancellationToken);
            decrypted.Position = 0;
            await shelf.SwapContentAsync(file, decrypted, encrypted: false, cancellationToken);
            result.Add(file.SizeBytes);
        }

        /// <exception cref="UsageException">The password does not open some of <paramref name="files"/>, which it names.</exception>
        /// <exception cref="AgeException">One of them cannot be read as an age file a password opens, which it names.</exception>
        private static async Task RequireAllOpenedByAsync(IShelfClient shelf, IEnumerable<ShelfFile> files, string password, CancellationToken cancellationToken)
        {
            List<string> others = new List<string>();
            foreach (ShelfFile file in files)
            {
                try
                {
                    if (!await IsOpenedByAsync(shelf, file, password, cancellationToken))
                    {
                        others.Add($"'{file.Path}'");
                    }
                }
                catch (AgeException exception)
                {
                    throw new AgeException(exception.Failure, $"Nothing was encrypted: '{file.Path}' could not be checked against the password. {exception.Message}");
                }
            }

            if (others.Count > 0)
            {
                throw new UsageException($"Nothing was encrypted: {string.Join(", ", others)} {(others.Count == 1 ? "is" : "are")} encrypted with another password. Decrypt {(others.Count == 1 ? "it" : "them")} with that one first.");
            }
        }

        private static async Task<bool> IsOpenedByAsync(IShelfClient shelf, ShelfFile file, string password, CancellationToken cancellationToken)
        {
            await using Stream content = await shelf.OpenReadAsync(file.Id, cancellationToken);
            return await AgeEncryption.IsOpenedByAsync(content, password, cancellationToken);
        }

        private static async Task<List<ShelfFile>> FilesUnderAsync(IShelfClient shelf, string directoryPath, CancellationToken cancellationToken)
        {
            return (await TreeUnderAsync(shelf, directoryPath, cancellationToken)).Files;
        }

        /// <summary>
        /// Every file under the directory, and the first encrypted directory below it, if any. The whole
        /// list is read before anything is swapped, so the run works from one listing, and each swap is
        /// refused if its file changed since then.
        /// </summary>
        private static async Task<(List<ShelfFile> Files, ShelfDirectory? EncryptedBelow)> TreeUnderAsync(IShelfClient shelf, string directoryPath, CancellationToken cancellationToken)
        {
            List<ShelfFile> files = new List<ShelfFile>();
            ShelfDirectory? encryptedBelow = null;
            await foreach (DirectoryTree page in Entries.TreePagesAsync(shelf, directoryPath, TreeQuery.MaxLimit, cancellationToken))
            {
                files.AddRange(page.Files);
                encryptedBelow ??= page.Directories.FirstOrDefault(directory => directory.Encrypted);
            }

            return (files, encryptedBelow);
        }
    }
}
