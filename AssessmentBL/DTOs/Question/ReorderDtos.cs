namespace AssessmentBL.DTOs.Question
{
    /// <summary>
    /// The new order of a set of items, as the ABSOLUTE list of their ids from
    /// first to last.
    ///
    /// This replaces the pairwise "swap these two" call, which was not idempotent:
    /// a swap applied twice — a client retry after a timeout, a double tap — puts
    /// the two items back where they started, so the admin's screen and the
    /// database silently disagree. Sending the whole order instead means the
    /// request describes a STATE, not a change: applying it ten times leaves
    /// exactly the same order as applying it once.
    /// </summary>
    public class ReorderRequestDto
    {
        /// <summary>
        /// Every id of the set being ordered, exactly once, in the order wanted.
        /// A missing or unknown id is rejected rather than guessed at, so a stale
        /// screen cannot half-apply an order.
        /// </summary>
        public List<int> OrderedIds { get; set; } = new();
    }
}
