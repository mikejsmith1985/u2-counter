namespace Counter.Infrastructure.Erp;

using Counter.Domain.Availability;
using Counter.Domain.Catalogue;
using Counter.Infrastructure.Mcp;
using Counter.Infrastructure.MultiValue;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads a part's stock position across every branch.
/// </summary>
public sealed class AvailabilityReader(IErpReader erp, ILogger<AvailabilityReader> logger)
{
    private readonly IErpReader _erp = erp;
    private readonly ILogger<AvailabilityReader> _logger = logger;

    /// <summary>
    /// Read one part and where it can be had.
    /// </summary>
    /// <param name="partNumber">The part to look up.</param>
    /// <param name="cancellationToken">Abandons the read when the caller gives up.</param>
    /// <returns>The part with its branch positions.</returns>
    /// <exception cref="ErpRecordNotFoundException">If the part is not in the catalogue.</exception>
    public async Task<PartAvailability> ReadAsync(
        string partNumber,
        CancellationToken cancellationToken)
    {
        Part part = await ReadPartAsync(partNumber, cancellationToken);

        try
        {
            string raw = await _erp.ReadRecordAsync(
                ErpFiles.Inventory.Name, partNumber, cancellationToken);

            return new PartAvailability(
                part,
                InventoryRecordParser.Parse(raw),
                IsStockKnown: true);
        }
        catch (ErpRecordNotFoundException)
        {
            // The part exists but nothing has been counted for it. That is a
            // different answer from zero, and the caller must be able to tell
            // them apart: saying "none" when nobody has counted is worse than
            // saying "we do not know".
            _logger.LogInformation(
                "{PartNumber} has no inventory record; stock is unknown rather than zero",
                partNumber);

            return new PartAvailability(part, [], IsStockKnown: false);
        }
    }

    /// <summary>Read a part from the catalogue.</summary>
    public async Task<Part> ReadPartAsync(string partNumber, CancellationToken cancellationToken)
    {
        string raw = await _erp.ReadRecordAsync(
            ErpFiles.Product.Name, partNumber, cancellationToken);

        return ReadPart(partNumber, raw);
    }

    /// <summary>Build a part from its stored record.</summary>
    /// <param name="partNumber">The key, which is not itself a field.</param>
    /// <param name="raw">The record as stored.</param>
    public static Part ReadPart(string partNumber, string raw)
    {
        MultiValueRecord record = MultiValueRecord.Parse(raw);

        return new Part(
            PartNumber: partNumber,
            Description: record.Field(ErpFiles.Product.Description),
            Manufacturer: record.Field(ErpFiles.Product.Manufacturer),
            ManufacturerPartNumber: record.Field(ErpFiles.Product.ManufacturerPartNumber),
            UnitOfMeasure: record.Field(ErpFiles.Product.UnitOfMeasure),
            CategoryCode: record.Field(ErpFiles.Product.CategoryCode),
            ListPrice: ReadAmount(record.Field(ErpFiles.Product.ListPrice)),
            IsDiscontinued: record.Field(ErpFiles.Product.Status)
                .Equals("D", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Read a stored amount, treating an unreadable one as zero.
    /// </summary>
    /// <remarks>
    /// Zero rather than a failure, because one malformed price should not make a
    /// part impossible to look up. A zero list price is visibly wrong on screen,
    /// where a missing part is simply absent.
    /// </remarks>
    private static decimal ReadAmount(string stored) =>
        decimal.TryParse(stored, out decimal amount) ? amount : 0m;
}
