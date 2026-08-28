namespace Counter.Infrastructure.Erp;

using System.Collections.Concurrent;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;

/// <summary>
/// The branches, held in memory once read.
/// </summary>
/// <remarks>
/// Branch names and cities are reference data: a dozen records that change when
/// someone opens a depot. Holding them is the same judgement as the catalogue —
/// what exists may be cached, how much there is may not.
/// </remarks>
public sealed class BranchDirectory(IErpReader erp)
{
    private readonly IErpReader _erp = erp;
    private readonly ConcurrentDictionary<string, Branch?> _known = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Look up a branch by its code.
    /// </summary>
    /// <param name="branchCode">The code, as an inventory record names it.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <returns>
    /// The branch, or null when the directory does not hold it. Null is an
    /// answer rather than an error: inventory records do name branches the
    /// directory has never had, and the row must still be shown.
    /// </returns>
    public async Task<Branch?> FindAsync(string branchCode, CancellationToken cancellationToken)
    {
        if (_known.TryGetValue(branchCode, out Branch? cached))
        {
            return cached;
        }

        Branch? branch;
        try
        {
            string raw = await _erp.ReadRecordAsync(
                ErpFiles.Branch.Name, branchCode, cancellationToken);

            branch = Read(branchCode, raw);
        }
        catch (ErpRecordNotFoundException)
        {
            branch = null;
        }

        // Absence is remembered too, so an unknown code does not cost a round
        // trip on every row of every screen that mentions it.
        _known[branchCode] = branch;
        return branch;
    }

    /// <summary>Build a branch from its stored record.</summary>
    public static Branch Read(string branchCode, string raw)
    {
        MultiValueRecord record = MultiValueRecord.Parse(raw);

        return new Branch(
            Code: branchCode,
            Name: record.Field(ErpFiles.Branch.BranchName),
            City: record.Field(ErpFiles.Branch.City),
            Region: record.Field(ErpFiles.Branch.Region),
            Telephone: record.Field(ErpFiles.Branch.Telephone));
    }
}

/// <summary>A stocking location.</summary>
/// <param name="Code">Short code, as inventory records name it.</param>
/// <param name="Name">The branch's name.</param>
/// <param name="City">Where it is.</param>
/// <param name="Region">Which region it belongs to.</param>
/// <param name="Telephone">How to reach it.</param>
public sealed record Branch(
    string Code,
    string Name,
    string City,
    string Region,
    string Telephone);
