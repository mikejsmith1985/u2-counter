namespace Counter.IntegrationTests;

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
            .Select(file => Path.GetFileName(file))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"The forbidden tool '{toolName}' is named in: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void Every_permitted_tool_is_actually_called_by_the_reader()
    {
        // A permission granted for no reason is one nobody will question later.
        // If a name here is never used, either the list or the reader is wrong.
        string reader = File.ReadAllText(Path.Combine(InfrastructureRoot(), "Mcp", "ErpReader.cs"));

        List<string> unused = ErpTools.Permitted
            .Where(tool => !reader.Contains(ToolConstantName(tool), StringComparison.Ordinal))
            .ToList();

        Assert.True(
            unused.Count == 0,
            $"These tools are permitted but never called: {string.Join(", ", unused)}.");
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
