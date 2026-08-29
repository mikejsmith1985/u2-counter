namespace Counter.Infrastructure.Mcp;

/// <summary>
/// The MCP tools a write may call, kept apart from the ones a read may.
/// </summary>
/// <remarks>
/// A second list rather than an addition to <see cref="ErpTools.Permitted"/>, and
/// the separation carries the guarantee.
///
/// Anything holding an <see cref="IErpReader"/> can only reach the read list, so
/// the sentence "the reader has no write path" stays true of the reader rather
/// than becoming a claim about how carefully somebody wrote a condition. A
/// deployment that never registers <see cref="IErpWriter"/> — which is every
/// deployment of this demonstration — has no code able to name anything here.
///
/// It is one tool. There is no delete and no create: the flow this supports is
/// "find a record, review it, change one value in it", which is what a counter
/// screen would ever legitimately do. Broader write capability would be a broader
/// claim, and this project's habit is to permit exactly what is used.
/// </remarks>
public static class ErpWriteTools
{
    /// <summary>Change one value of one field, leaving other positions alone.</summary>
    /// <remarks>
    /// Not `write_record`. Handing the server a whole rebuilt record puts the
    /// responsibility for parallel-field alignment on whoever assembled it, and
    /// that is precisely the thing that goes wrong silently. Naming the field and
    /// the value keeps the record's shape the server's problem, where it can be
    /// enforced once.
    /// </remarks>
    public const string UpdateValue = "update_value";

    /// <summary>Every tool a write may call.</summary>
    public static readonly IReadOnlySet<string> Permitted = new HashSet<string>(
        StringComparer.Ordinal)
    {
        UpdateValue,
    };
}
