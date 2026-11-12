namespace PSAI
{
    internal static class StorageCatalogPolicy
    {
        internal static bool CanReadContents(bool owned, bool normalGrid, bool tracked, bool opened) =>
            owned && normalGrid && (!tracked || opened);

        internal static bool IsCandidate(bool matches, bool requiresOpen, bool canRemove, bool inOffer) =>
            matches && (requiresOpen || canRemove || inOffer);
    }
}
