using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.BSA.Core
{
    /// <summary>
    /// One wizard page: a slice of checks from a single group (or the synthetic auto-checks group),
    /// in authored order. Purely a display-order view - never mutates or reorders the checks it
    /// references. See PreflightPagePlan.Build's "do not reorder Run.Checks" invariant.
    /// </summary>
    public class PreflightPage
    {
        public string GroupName { get; }
        public int PageInGroup { get; }
        public int PagesInGroup { get; }
        public bool IsAutoPage { get; }
        public IReadOnlyList<PreflightCheckDefinition> Checks { get; }

        public PreflightPage(string groupName, int pageInGroup, int pagesInGroup, bool isAutoPage,
            IReadOnlyList<PreflightCheckDefinition> checks)
        {
            GroupName = groupName;
            PageInGroup = pageInGroup;
            PagesInGroup = pagesInGroup;
            IsAutoPage = isAutoPage;
            Checks = checks;
        }
    }

    /// <summary>
    /// Pure function from a checklist's checks + metadata to an ordered list of wizard pages - same
    /// shape as PreflightAggregator (no UI, no globals, trusts loader-validated input). Never
    /// reorders or mutates the input list; only ever produces a display-order view over it. See
    /// WP1_wizard_grouping_pagination_plan.md §2 for the "do not reorder Run.Checks" rationale -
    /// PreflightRun.Checks is IReadOnlyList&lt;&gt; precisely so nothing downstream of this class can
    /// be tempted to feed a reordered list back into the run.
    /// </summary>
    public static class PreflightPagePlan
    {
        /// <summary>Group name used when Metadata.Groups is not declared - the whole checklist
        /// (minus any hoisted Auto checks) renders as one implicit group, still paginated.</summary>
        public const string ImplicitGroupName = "Checks";

        public static IReadOnlyList<PreflightPage> Build(IReadOnlyList<PreflightCheckDefinition> checks,
            PreflightChecklistMetadata metadata)
        {
            if (checks == null) throw new ArgumentNullException(nameof(checks));
            metadata = metadata ?? new PreflightChecklistMetadata();

            var pages = new List<PreflightPage>();
            var pageSize = Math.Max(1, metadata.PageSize);
            var autoPageSize = Math.Max(1, metadata.AutoPageSize);

            IEnumerable<PreflightCheckDefinition> remaining = checks;

            if (metadata.AutoChecksFirst)
            {
                var autoChecks = checks.Where(c => c.Type == CheckType.Auto).ToList();
                remaining = checks.Where(c => c.Type != CheckType.Auto);

                if (autoChecks.Count > 0)
                {
                    var autoGroupName = string.IsNullOrWhiteSpace(metadata.AutoGroupTitle)
                        ? "System checks"
                        : metadata.AutoGroupTitle;
                    AddPagesForGroup(pages, autoGroupName, autoChecks, autoPageSize, isAutoPage: true);
                }
            }

            var declaredGroups = metadata.Groups ?? new List<string>();

            if (declaredGroups.Count > 0)
            {
                var byGroup = new Dictionary<string, List<PreflightCheckDefinition>>(StringComparer.OrdinalIgnoreCase);
                foreach (var check in remaining)
                {
                    if (check.Group == null) continue; // loader-invalid input; nothing sane to place it under
                    if (!byGroup.TryGetValue(check.Group, out var list))
                        byGroup[check.Group] = list = new List<PreflightCheckDefinition>();
                    list.Add(check);
                }

                foreach (var groupName in declaredGroups)
                {
                    // A declared group with zero checks is skipped, not an error - legitimate while a
                    // checklist is being edited down (see the plan's loader validation table).
                    if (byGroup.TryGetValue(groupName, out var groupChecks) && groupChecks.Count > 0)
                        AddPagesForGroup(pages, groupName, groupChecks, pageSize, isAutoPage: false);
                }
            }
            else
            {
                var remainingList = remaining.ToList();
                if (remainingList.Count > 0)
                    AddPagesForGroup(pages, ImplicitGroupName, remainingList, pageSize, isAutoPage: false);
            }

            return pages;
        }

        static void AddPagesForGroup(List<PreflightPage> pages, string groupName,
            IReadOnlyList<PreflightCheckDefinition> groupChecks, int pageSize, bool isAutoPage)
        {
            var chunks = BalancedChunks(groupChecks, pageSize);
            for (var i = 0; i < chunks.Count; i++)
                pages.Add(new PreflightPage(groupName, i + 1, chunks.Count, isAutoPage, chunks[i]));
        }

        /// <summary>Splits a group into pages as evenly as possible rather than greedily filling each
        /// page to pageSize - 13 checks at page size 5 becomes 5,4,4, never 5,5,3, and no page ever
        /// ends up with a single orphaned item just because pageSize didn't divide evenly.</summary>
        static List<List<PreflightCheckDefinition>> BalancedChunks(
            IReadOnlyList<PreflightCheckDefinition> items, int pageSize)
        {
            var n = items.Count;
            var pageCount = Math.Max(1, (int)Math.Ceiling(n / (double)pageSize));

            var chunks = new List<List<PreflightCheckDefinition>>(pageCount);
            var baseSize = n / pageCount;
            var remainder = n % pageCount;
            var index = 0;

            for (var p = 0; p < pageCount; p++)
            {
                var size = baseSize + (p < remainder ? 1 : 0);
                chunks.Add(items.Skip(index).Take(size).ToList());
                index += size;
            }

            return chunks;
        }
    }
}
