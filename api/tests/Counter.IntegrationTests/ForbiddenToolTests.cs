namespace Counter.IntegrationTests;

using System.Text.RegularExpressions;
using Counter.Infrastructure.Mcp;

/// <summary>
/// The tools this application must never call.
/// </summary>
/// <remarks>
/// A restriction that lives only in a document is a comment. This reads the
/// infrastructure source and fails the build if a forbidden tool name appears
/// anywhere outside the file that lists them, so adding a call is not something
/// anyone can do quietly.
///
/// Source rather than the compiled assembly, deliberately. String constants are
/// inlined by the compiler, so a scan of the assembly finds the allowed names
/// (which are used) and the forbidden ones (which the list itself declares) mixed
/// together with no way to tell which came from a call. The source has the
/// distinction the check needs, and a reviewer can run the same grep by hand.
/// </remarks>
public sealed class ForbiddenToolTests
{
    /// <summary>Where the tool names may legitimately appear.</summary>
    /// <remarks>
    /// One file. Naming the forbidden tools is that file's whole purpose, and
    /// excluding it by name rather than by pattern means a second file cannot
    /// quietly acquire the same exemption.
    /// </remarks>
    private const string DeclarationFile = "ErpTools.cs";

    [Fact]
    public void The_forbidden_list_is_not_empty()
    {
        // A test that iterates an empty list passes while proving nothing.
        Assert.NotEmpty(ErpTools.Forbidden);
    }

    [Fact]
    public void Every_forbidden_tool_carries_a_reason()
    {
        // A rule with no stated reason is one the next person will remove.
        Assert.All(ErpTools.Forbidden, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
    }

    [Fact]
    public void No_permitted_tool_is_also_forbidden()
    {
        // Two lists that disagree would let a reviewer read either one and be
        // told something different.
        Assert.Empty(ErpTools.Permitted.Intersect(ErpTools.Forbidden.Keys));
    }

    [Theory]
    [MemberData(nameof(ForbiddenToolNames))]
    public void No_forbidden_tool_name_appears_anywhere_in_the_infrastructure(string toolName)
    {
        string quoted = $"\"{toolName}\"";

        List<string> offenders = InfrastructureSources()
            .Where(file => !string.Equals(
                Path.GetFileName(file), DeclarationFile, StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains(quoted, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Select(name => name!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"The forbidden tool '{toolName}' is named in: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void Every_permitted_tool_is_named_by_the_reader()
    {
        // The weaker of the two checks, and the one that used to be the only one.
        // It says the reader mentions each permitted tool. It does not say
        // anything reaches the method that mentions it.
        string reader = File.ReadAllText(Path.Combine(InfrastructureRoot(), "Mcp", "ErpReader.cs"));

        List<string> unnamed = ErpTools.Permitted
            .Where(tool => !MentionsWord(reader, ToolConstantName(tool)))
            .ToList();

        Assert.True(
            unnamed.Count == 0,
            $"These tools are permitted but the reader never names them: {string.Join(", ", unnamed)}.");
    }

    [Fact]
    public void Every_method_on_the_reader_has_a_caller()
    {
        // This is the check that matters, and its absence let a permission sit
        // unused for weeks.
        //
        // `execute_query` -- the arbitrary-query tool, the most capability any
        // entry on this list could grant -- was permitted and called by nothing.
        // The test above passed the whole time, because `ErpReader` had a
        // `QueryAsync` method that named the constant, and nothing invoked
        // `QueryAsync`. A grep for a name cannot tell a live path from a dead one.
        //
        // So the reader's own surface is checked instead: every method it offers
        // must be reached from somewhere outside the file that defines it. A
        // method nobody calls is a tool nobody needs, and the permission behind it
        // should go with the code.
        IEnumerable<string> methods = typeof(IErpReader)
            .GetMethods()
            .Select(method => method.Name);

        string callers = string.Concat(
            SourceFilesExcept("IErpReader.cs", "ErpReader.cs").Select(File.ReadAllText));

        List<string> uncalled = methods
            .Where(method => !MentionsWord(callers, method))
            .ToList();

        Assert.True(
            uncalled.Count == 0,
            $"These reader methods are defined and never called: {string.Join(", ", uncalled)}. " +
            "Remove them, and the tool permission each one carries.");
    }

    /// <summary>The forbidden names, as theory data.</summary>
    public static TheoryData<string> ForbiddenToolNames()
    {
        TheoryData<string> data = [];

        foreach (string name in ErpTools.Forbidden.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>Turn a tool name into the constant the reader refers to it by.</summary>
    /// <remarks>
    /// `read_record` is declared as `ReadRecord`, so the source is searched for
    /// the constant rather than the literal -- which is what the reader actually
    /// writes, and what a reviewer would look for.
    /// </remarks>
    private static string ToolConstantName(string toolName) =>
        string.Concat(toolName.Split('_').Select(
            part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>
    /// Whether a name appears in source as a whole word.
    /// </summary>
    /// <remarks>
    /// A plain substring search cannot tell `ReadRecord` from `ReadRecords`, so
    /// removing the first while keeping the second would leave every check here
    /// passing. The boundary is what makes each name answer for itself.
    /// </remarks>
    private static bool MentionsWord(string source, string name) =>
        Regex.IsMatch(source, $@"\b{Regex.Escape(name)}\b");

    /// <summary>Every C# source file in the project except the ones named.</summary>
    private static IEnumerable<string> SourceFilesExcept(params string[] excluded) =>
        InfrastructureSources()
            .Concat(Directory.EnumerateFiles(
                Path.Combine(CounterFixture.RepositoryRoot, "api", "src", "Counter.Api"),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !excluded.Contains(Path.GetFileName(file), StringComparer.Ordinal));

    /// <summary>Every C# file in the infrastructure project.</summary>
    private static IEnumerable<string> InfrastructureSources() =>
        Directory.EnumerateFiles(InfrastructureRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

    /// <summary>Where the infrastructure project's source lives.</summary>
    private static string InfrastructureRoot()
    {
        string root = Path.Combine(
            CounterFixture.RepositoryRoot, "api", "src", "Counter.Infrastructure");

        Assert.True(Directory.Exists(root), $"The infrastructure source was not found at {root}.");

        return root;
    }
}
