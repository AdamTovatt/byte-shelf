using System.IO;

namespace ByteShelfCommon
{
    public interface IContentProvider
    {
        Stream GetStream();
    }
} 