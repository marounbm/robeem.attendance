using System.Text.RegularExpressions;

namespace robeem.Tests.Web;

/// <summary>
/// A partial a view names must be in its own screen's folder or in Views/Shared, or the page fails when it is drawn. The controller tests never draw a
/// view, so a partial that sits in another screen's folder is only found when somebody opens the page; this finds it here.
/// </summary>
public sealed partial class ViewPartialsTests
{
    [Fact]
    public void EveryPartialAViewNames_IsInTheViewsOwnFolderOrInShared()
    {
        var views = FindViewsFolder();
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories))
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(file))!;
            foreach (Match match in PartialTag().Matches(File.ReadAllText(file)))
            {
                var name = match.Groups["name"].Value;

                // A name with a path (or with @) is resolved as given; only a bare name is looked for by the view engine's rules.
                if (name.Contains('/') || name.Contains('@'))
                {
                    continue;
                }

                var own = Path.Combine(views, folder, name + ".cshtml");
                var shared = Path.Combine(views, "Shared", name + ".cshtml");
                if (!File.Exists(own) && !File.Exists(shared))
                {
                    missing.Add($"{Path.GetRelativePath(views, file)} names the partial '{name}', which is in neither Views/{folder} nor Views/Shared");
                }
            }
        }

        Assert.Empty(missing);
    }

    [GeneratedRegex("""<partial\s+name="(?<name>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PartialTag();

    private static string FindViewsFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", "robeem.Web", "Views");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("The Web project's Views folder was not found above the test output.");
    }
}
