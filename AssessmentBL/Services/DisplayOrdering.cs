using Shared.Common.Exceptions;

namespace AssessmentBL.Services
{
    /// <summary>
    /// The one check every absolute-reorder endpoint makes before it renumbers
    /// anything: the list the admin sent must describe the WHOLE set, each item
    /// exactly once.
    ///
    /// Refusing a partial or stale list is the point. A reorder that quietly
    /// skipped unknown ids, or left out the ones the client forgot, would write
    /// half an order and leave the rest wherever it happened to be — the same
    /// kind of silent divergence the pairwise swap caused. Rejecting it tells the
    /// admin to reload, which is the only safe answer to a stale screen.
    /// </summary>
    public static class DisplayOrdering
    {
        public static void EnsureCoversExactly(
            IReadOnlyList<int> orderedIds,
            IEnumerable<int> existingIds,
            string itemsLabel,
            string ownerLabel)
        {
            var existing = existingIds.ToHashSet();

            if (orderedIds.Count == 0)
                throw new ArgumentException(
                    $"قائمة ترتيب {itemsLabel} فارغة", nameof(orderedIds));

            var seen = new HashSet<int>(orderedIds.Count);

            foreach (var id in orderedIds)
            {
                if (!seen.Add(id))
                    throw new ArgumentException(
                        $"العنصر رقم {id} مكرر في قائمة ترتيب {itemsLabel}", nameof(orderedIds));

                if (!existing.Contains(id))
                    throw new ArgumentException(
                        $"العنصر رقم {id} لا يخص {ownerLabel}", nameof(orderedIds));
            }

            var missing = existing.Where(id => !seen.Contains(id)).OrderBy(id => id).ToList();

            if (missing.Count > 0)
                throw new BusinessRuleException(
                    $"يجب إرسال ترتيب {itemsLabel} كاملة؛ العناصر الناقصة: {string.Join(", ", missing)}");
        }
    }
}
