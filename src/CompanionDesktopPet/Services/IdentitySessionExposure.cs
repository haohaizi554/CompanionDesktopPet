namespace CompanionDesktopPet.Services;

/// <summary>
/// Keeps the authored identity-name exposure rules local to one running companion session.
/// It intentionally has no snapshot or persistence surface: restarting the companion starts
/// a fresh identity exposure session.
/// </summary>
internal sealed class IdentitySessionExposure
{
    private readonly object _sync = new();
    private readonly Queue<string> _recentSemanticGroups = [];
    private readonly Dictionary<string, int> _directMarkerUses = new(StringComparer.Ordinal);

    internal bool IsEligible(DialogueLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var markerClasses = line.IdentityMarkerClasses;
        if (!IsDirectMarkerLine(line, markerClasses))
        {
            return true;
        }

        lock (_sync)
        {
            var policy = PersonaContractGenerated.AuthoredIdentity;
            return markerClasses.All(marker =>
                       _directMarkerUses.GetValueOrDefault(marker) < policy.DirectMarkerMaxPerIdentityClass)
                   && MeetsMinimumInterveningBubblesLocked(
                       line.SemanticGroup,
                       policy.MinimumInterveningBubblesSameSemanticGroup)
                   && !_recentSemanticGroups.Contains(line.SemanticGroup);
        }
    }

    internal bool MeetsMinimumInterveningBubbles(string semanticGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticGroup);
        lock (_sync)
        {
            return MeetsMinimumInterveningBubblesLocked(
                semanticGroup,
                PersonaContractGenerated.AuthoredIdentity.MinimumInterveningBubblesSameSemanticGroup);
        }
    }

    internal void Record(DialogueLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var markerClasses = line.IdentityMarkerClasses;
        lock (_sync)
        {
            if (IsDirectMarkerLine(line, markerClasses))
            {
                foreach (var marker in markerClasses.Distinct(StringComparer.Ordinal))
                {
                    _directMarkerUses[marker] = _directMarkerUses.GetValueOrDefault(marker) + 1;
                }
            }

            _recentSemanticGroups.Enqueue(line.SemanticGroup);
            var policy = PersonaContractGenerated.AuthoredIdentity;
            var retainedCount = Math.Max(
                policy.RecentBubblesPerSemanticGroup,
                policy.MinimumInterveningBubblesSameSemanticGroup + 1);
            while (_recentSemanticGroups.Count > retainedCount)
            {
                _recentSemanticGroups.Dequeue();
            }
        }
    }

    private bool MeetsMinimumInterveningBubblesLocked(string semanticGroup, int minimumInterveningBubbles)
    {
        var groups = _recentSemanticGroups.ToArray();
        for (var index = groups.Length - 1; index >= 0; index--)
        {
            if (!string.Equals(groups[index], semanticGroup, StringComparison.Ordinal))
            {
                continue;
            }

            return groups.Length - index - 1 >= minimumInterveningBubbles;
        }

        return true;
    }

    private static bool IsDirectMarkerLine(
        DialogueLine line,
        IReadOnlyList<string> markerClasses)
    {
        if (markerClasses.Count == 0
            || line.SourceKind != "curated_standalone"
            || !line.SourceReference.StartsWith("authored:", StringComparison.Ordinal))
        {
            return false;
        }

        var batchStart = "authored:".Length;
        var batchEnd = line.SourceReference.IndexOf(';', batchStart);
        var batchId = batchEnd >= 0
            ? line.SourceReference[batchStart..batchEnd]
            : line.SourceReference[batchStart..];
        return PersonaContractGenerated.AuthoredIdentity.DirectMarkerBatchById.TryGetValue(
                   batchId,
                   out var assignedMarker)
               && markerClasses.Contains(assignedMarker, StringComparer.Ordinal);
    }
}
