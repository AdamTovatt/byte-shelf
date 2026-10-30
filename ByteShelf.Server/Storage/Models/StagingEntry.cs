namespace ByteShelf.Server.Storage.Models
{
    /// <summary>One entry in <c>staging/</c> and the upload id it is named for, when its name is one.</summary>
    public record StagingEntry(string Path, Guid? UploadId);
}
