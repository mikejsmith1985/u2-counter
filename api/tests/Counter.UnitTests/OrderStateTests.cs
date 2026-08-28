namespace Counter.UnitTests;

using Counter.Domain.Orders;

/// <summary>
/// Which order states hold stock, and what happens to a value nobody recognises.
/// </summary>
public sealed class OrderStateTests
{
    [Theory]
    [InlineData("CONFIRMED", OrderState.Confirmed)]
    [InlineData("ALLOCATED", OrderState.Allocated)]
    [InlineData("PICKING", OrderState.Picking)]
    public void States_that_hold_stock_are_recognised(string stored, OrderState expected)
    {
        Assert.Equal(expected, OrderStates.Parse(stored));
        Assert.True(OrderStates.Parse(stored).HoldsStock());
    }

    [Theory]
    [InlineData("QUOTE")]
    [InlineData("SHIPPED")]
    [InlineData("CANCELLED")]
    public void States_that_hold_nothing_do_not_hold_stock(string stored)
    {
        // A quote is not a promise; shipped stock has left; a cancelled order was
        // never taken. Counting any of them would understate what can be sold.
        Assert.False(OrderStates.Parse(stored).HoldsStock());
    }

    [Theory]
    [InlineData("CONFIMED")]
    [InlineData("in progress")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_state_holds_nothing(string? stored)
    {
        // The safe reading of a value nobody recognises is that it makes no claim
        // on stock. Treating it as holding stock would let a typo in ERP data
        // understate availability and cost a sale.
        Assert.Equal(OrderState.Unrecognised, OrderStates.Parse(stored));
        Assert.False(OrderStates.Parse(stored).HoldsStock());
    }

    [Fact]
    public void Reading_a_state_ignores_case_and_surrounding_space()
    {
        Assert.Equal(OrderState.Allocated, OrderStates.Parse("  allocated  "));
    }

    [Fact]
    public void Exactly_three_states_hold_stock()
    {
        // Guards the closed set: adding a fourth should be a deliberate decision
        // with a test change, not something that slips in.
        Assert.Equal(3, OrderStates.HoldingStock.Count);
    }
}
