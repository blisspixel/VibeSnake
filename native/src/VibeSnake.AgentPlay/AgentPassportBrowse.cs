namespace VibeSnake.AgentPlay;

/// <summary>
/// One local public-identity record as a screen row. Every field is a fact
/// the receipts earned. The caller-declared display name is absent on
/// purpose: a name is a claim, and this row is a record.
/// </summary>
public sealed record AgentPassportBrowseEntryV1(
    string Schema,
    int Position,
    string AgentId,
    int ExhibitionCount,
    int BestScore,
    int PolicyCount,
    int DivisionCount,
    int RivalryCount,
    int AheadCount,
    int LevelCount,
    int BehindCount,
    int MilestoneCount,
    string FirstReceiptHash,
    string LatestReceiptHash)
{
    public const string Contract = "vibesnake-agent-passport-browse-entry-v1";
}

/// <summary>
/// The browse view over the local passport store: what a person can look at
/// without treating ahead, level, or behind as a standing, and without
/// inventing a display name the store refused to keep.
///
/// Building this never writes. Confirm on the Godot screen opens the latest
/// receipt in exhibitions, which is a handoff, not a ranking.
/// </summary>
public sealed record AgentPassportBrowseReportV1(
    string Schema,
    int RecordCount,
    int ExhibitionTotal,
    int RemainingRecords,
    int SelectedIndex,
    IReadOnlyList<AgentPassportBrowseEntryV1> Entries)
{
    public const string Contract = "vibesnake-agent-passport-browse-report-v1";

    public bool IsEmpty => RecordCount == 0;

    public AgentPassportBrowseEntryV1? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Entries.Count ? Entries[SelectedIndex] : null;

    /// <summary>
    /// The latest recorded exhibition a Confirm handoff should open, or null
    /// when nothing is selected.
    /// </summary>
    public string? HandoffReceiptHash => Selected?.LatestReceiptHash;

    public static AgentPassportBrowseReportV1 Create(
        AgentPassportDocumentV1 document,
        int selectedIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        var entries = document.Records
            .Select((record, position) => new AgentPassportBrowseEntryV1(
                AgentPassportBrowseEntryV1.Contract,
                position,
                record.AgentId,
                record.Exhibitions,
                record.BestScore,
                record.PolicyVersions.Count,
                record.DivisionIds.Count,
                record.Rivals.Count,
                record.Rivals.Sum(rival => rival.Ahead),
                record.Rivals.Sum(rival => rival.Level),
                record.Rivals.Sum(rival => rival.Behind),
                record.Milestones.Count,
                record.FirstReceiptHash,
                record.LatestReceiptHash))
            .ToArray();
        var bounded = entries.Length == 0
            ? -1
            : Math.Clamp(selectedIndex, 0, entries.Length - 1);
        return new AgentPassportBrowseReportV1(
            Contract,
            entries.Length,
            entries.Sum(entry => entry.ExhibitionCount),
            Math.Max(0, document.Capacity - entries.Length),
            bounded,
            entries);
    }

    /// <summary>
    /// Moves the selection without wrapping past either end, so a person holding
    /// a direction never loops silently back to where they started.
    /// </summary>
    public AgentPassportBrowseReportV1 WithSelection(int index) =>
        Entries.Count == 0
            ? this with { SelectedIndex = -1 }
            : this with { SelectedIndex = Math.Clamp(index, 0, Entries.Count - 1) };
}
