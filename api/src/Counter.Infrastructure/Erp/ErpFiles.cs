namespace Counter.Infrastructure.Erp;

/// <summary>
/// The ERP files this application reads, and where each piece of information sits.
/// </summary>
/// <remarks>
/// Field positions live here rather than scattered through the readers. A layout
/// stated once can be checked against the contract; the same number appearing in
/// four places is four opportunities for one of them to drift.
/// </remarks>
public static class ErpFiles
{
    /// <summary>The catalogue.</summary>
    public static class Product
    {
        /// <summary>File name.</summary>
        public const string Name = "PRODUCT";

        /// <summary>What the part is.</summary>
        public const int Description = 1;

        /// <summary>Who makes it.</summary>
        public const int Manufacturer = 2;

        /// <summary>Their own number for it.</summary>
        public const int ManufacturerPartNumber = 3;

        /// <summary>How it is sold.</summary>
        public const int UnitOfMeasure = 4;

        /// <summary>Joins to contract pricing.</summary>
        public const int CategoryCode = 5;

        /// <summary>The undiscounted price.</summary>
        public const int ListPrice = 6;

        /// <summary>A for active, D for discontinued.</summary>
        public const int Status = 7;
    }

    /// <summary>Stock positions, held as parallel fields keyed by part number.</summary>
    public static class Inventory
    {
        /// <summary>File name.</summary>
        public const string Name = "INVENTORY";
    }

    /// <summary>Stocking locations.</summary>
    public static class Branch
    {
        /// <summary>File name.</summary>
        public const string Name = "BRANCH";

        /// <summary>The branch's name.</summary>
        public const int BranchName = 1;

        /// <summary>Where it is.</summary>
        public const int City = 2;

        /// <summary>Which region it belongs to.</summary>
        public const int Region = 3;

        /// <summary>Telephone number.</summary>
        public const int Telephone = 4;
    }

    /// <summary>Customer accounts.</summary>
    public static class Customer
    {
        /// <summary>File name.</summary>
        public const string Name = "CUSTOMER";

        /// <summary>Trading name.</summary>
        public const int CustomerName = 1;

        /// <summary>Address lines, multivalued.</summary>
        public const int AddressLines = 2;

        /// <summary>Contact names, multivalued.</summary>
        public const int ContactNames = 3;

        /// <summary>Telephone numbers, multivalued and subvalued by contact.</summary>
        public const int ContactNumbers = 4;

        /// <summary>Payment terms.</summary>
        public const int PaymentTerms = 5;

        /// <summary>Joins to contract pricing.</summary>
        public const int PriceClass = 6;

        /// <summary>Where they normally collect from.</summary>
        public const int HomeBranch = 7;
    }

    /// <summary>Contract terms, keyed by price class and category.</summary>
    public static class Pricing
    {
        /// <summary>File name.</summary>
        public const string Name = "PRICING";

        /// <summary>Multipliers against list price, multivalued.</summary>
        public const int Multipliers = 1;

        /// <summary>First day each set of terms applies, multivalued.</summary>
        public const int EffectiveFrom = 2;

        /// <summary>Last day each set of terms applies, multivalued.</summary>
        public const int EffectiveTo = 3;

        /// <summary>Build the key for a price class and category.</summary>
        /// <param name="priceClass">The customer's price class.</param>
        /// <param name="categoryCode">The part's category.</param>
        public static string KeyFor(string priceClass, string categoryCode) =>
            $"{priceClass}*{categoryCode}";
    }

    /// <summary>Orders, with line items held as parallel fields.</summary>
    public static class Order
    {
        /// <summary>File name.</summary>
        public const string Name = "ORDER";

        /// <summary>Whose order it is.</summary>
        public const int CustomerAccount = 1;

        /// <summary>When it was placed.</summary>
        public const int OrderDate = 2;

        /// <summary>Where it stands, and therefore whether it holds stock.</summary>
        public const int State = 3;

        /// <summary>Part numbers, multivalued.</summary>
        public const int LineParts = 4;

        /// <summary>Quantities, multivalued.</summary>
        public const int LineQuantities = 5;

        /// <summary>Fulfilling branches, multivalued.</summary>
        public const int LineBranches = 6;

        /// <summary>Promised dates, multivalued.</summary>
        public const int LinePromisedDates = 7;
    }
}
