using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <inheritdoc />
    internal sealed class DerivedListing : IDerivedListing
    {
        private readonly DerivedMakers _makers;
        private readonly DerivedStore _store;

        public DerivedListing(DerivedMakers makers, DerivedStore store)
        {
            _makers = makers;
            _store = store;
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<DerivedKind, string>? Of(FileRecord file)
        {
            List<DerivedKind> kinds = DerivedKinds.All.Where(kind => _makers.MayHave(file, kind)).ToList();
            if (kinds.Count == 0)
            {
                return null;
            }

            IReadOnlyDictionary<DerivedKind, DerivedEntry> entries = _store.FindAll(file.Sha256);
            Dictionary<DerivedKind, string> made = new Dictionary<DerivedKind, string>();
            foreach (DerivedKind kind in kinds)
            {
                if (entries.TryGetValue(kind, out DerivedEntry? entry) && entry.Outcome is DerivedOutcome.Made && _makers.IsCurrent(kind, entry))
                {
                    made.Add(kind, entry.Tag);
                }
            }

            return made.Count == 0 ? null : made;
        }
    }
}
