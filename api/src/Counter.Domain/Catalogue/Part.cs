namespace Counter.Domain.Catalogue;

/// <summary>
/// An item in the catalogue, as the ERP holds it.
/// </summary>
/// <param name="PartNumber">The key, unique across the catalogue.</param>
/// <param name="Description">What the part is, in the words a counter uses.</param>
/// <param name="Manufacturer">Who makes it.</param>
/// <param name="ManufacturerPartNumber">Their own number for it, which customers often quote.</param>
/// <param name="UnitOfMeasure">How it is sold: each, per foot, by the box.</param>
/// <param name="CategoryCode">Joins to contract pricing.</param>
/// <param name="ListPrice">The undiscounted price.</param>
/// <param name="IsDiscontinued">
/// Discontinued parts are still shown, marked as such. Customers ask for parts
/// that were discontinued last month, and hiding them answers the wrong question.
/// </param>
public sealed record Part(
    string PartNumber,
    string Description,
    string Manufacturer,
    string ManufacturerPartNumber,
    string UnitOfMeasure,
    string CategoryCode,
    decimal ListPrice,
    bool IsDiscontinued);
