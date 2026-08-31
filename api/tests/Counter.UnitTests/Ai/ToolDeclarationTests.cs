namespace Counter.UnitTests.Ai;

using System.Reflection;
using Counter.Infrastructure.Ai;

/// <summary>
/// The tools the assistant is offered, against the tools it can actually run.
/// </summary>
/// <remarks>
/// Two lists have to agree and nothing makes them: the definitions handed to
/// the model, and the switch that dispatches a call by name. A tool declared
/// and not dispatched is worse than a missing feature -- the model reads the
/// description, decides it is the right tool, calls it, and is told the call
/// failed. It then usually apologises and answers from nothing.
///
/// A tool dispatched and not declared is the quieter half: the code is
/// unreachable and the capability silently absent, which is how "it can't look
/// up pricing" happened on a deployment that could.
///
/// Neither is a compile error and neither shows up in a passing request.
/// </remarks>
public sealed class ToolDeclarationTests
{
    /// <summary>Every tool name declared as a constant on the tool list.</summary>
    /// <returns>The names, as the model would see them.</returns>
    private static IReadOnlyList<string> DeclaredNames() =>
        [.. typeof(AskTools)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    [Fact]
    public void Every_declared_tool_is_offered_to_the_model()
    {
        // A name constant with no definition beside it is a tool the code
        // dispatches and the model is never told about.
        IReadOnlyList<string> offered = [.. AskTools.Definitions.Select(tool => tool.Name)];

        Assert.All(DeclaredNames(), name => Assert.Contains(name, offered));
    }

    [Fact]
    public void Every_offered_tool_has_a_name_the_code_refers_to_by_constant()
    {
        // The other direction: a definition whose name is a loose string cannot
        // match the dispatch, which compares against these constants.
        IReadOnlyList<string> declared = DeclaredNames();

        Assert.All(AskTools.Definitions, tool => Assert.Contains(tool.Name, declared));
    }

    [Fact]
    public void No_tool_is_offered_twice()
    {
        // Two definitions under one name is ambiguous to the model and to the
        // dispatch, and neither reports it.
        IReadOnlyList<string> offered = [.. AskTools.Definitions.Select(tool => tool.Name)];

        Assert.Equal(offered.Count, offered.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_tool_describes_itself()
    {
        // The description is the only thing the model has to choose by. An
        // empty one is a tool it will not reach for, which looks exactly like
        // a capability that does not exist.
        Assert.All(AskTools.Definitions, tool =>
            Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    [Fact]
    public void No_tool_is_named_for_something_that_writes()
    {
        // The claim made everywhere about this deployment is that the assistant
        // reads and cannot write. That claim is enforced by the tools it is
        // given, so it is worth failing loudly if one ever arrives.
        string[] forbidden = ["write", "update", "delete", "create", "remove", "set"];

        Assert.All(AskTools.Definitions, tool =>
            Assert.DoesNotContain(forbidden, verb =>
                tool.Name.Contains(verb, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_readable_files_are_the_ones_the_screens_already_show()
    {
        // The assistant must not reach a file a person using the application
        // could not open for themselves. Widening this set is a decision, and
        // this makes it a deliberate one rather than an edit nobody noticed.
        Assert.Equal(
            ["BRANCH", "INVENTORY", "ORDER", "PRODUCT"],
            AskTools.ReadableFiles.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void The_pricing_files_are_not_readable_wholesale()
    {
        // Pricing and customer records are reached through tools that quote a
        // figure, not by handing the model the raw files. Contract terms are
        // commercially sensitive in a way a part's description is not.
        Assert.DoesNotContain("PRICING", AskTools.ReadableFiles);
        Assert.DoesNotContain("CUSTOMER", AskTools.ReadableFiles);
    }
}
