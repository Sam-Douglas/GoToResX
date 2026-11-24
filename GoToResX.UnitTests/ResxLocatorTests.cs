using GoToResX.Core;

namespace GoToResX.UnitTests;

public sealed class ResxLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GoToResX.Tests", Guid.NewGuid().ToString("N"));

    public ResxLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relativePath, string text = "")
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private const string IndexRazor = """
        @page "/"
        @inject IStringLocalizer<Index> Localizer

        <h1>@Localizer["Title"]</h1>
        """;

    private const string ProgramWithResourcesPath = """
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        """;

    private ResxLocation? Locate(string razorPath, string? caretLine = null) =>
        ResxLocator.Locate(razorPath, File.ReadAllText(razorPath), caretLine);

    [Fact]
    public void FindsResxUnderResourcesPathMirroringTheNamespace()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", ProgramWithResourcesPath);
        var razor = Write("Project.Web/Pages/Index.razor", IndexRazor);
        var resx = Write("Project.Web/Resources/Pages/Index.resx");
        Write("Project.Web/Resources/Pages/Index.fr.resx");

        var result = Locate(razor);

        Assert.Equal("Project.Web.Pages.Index", result!.LocalizerType);
        Assert.Equal("Project.Web.Resources.Pages.Index", result.ExpectedResourceName);
        Assert.Equal(resx, result.ResxPath);
    }

    [Fact]
    public void FindsDottedResxFileName()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", ProgramWithResourcesPath);
        var razor = Write("Project.Web/Pages/Index.razor", IndexRazor);
        var resx = Write("Project.Web/Resources/Pages.Index.resx");

        Assert.Equal(resx, Locate(razor)!.ResxPath);
    }

    [Fact]
    public void FindsResxNextToComponentWithoutResourcesPath()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", "builder.Services.AddLocalization();");
        var razor = Write("Project.Web/Pages/Index.razor", IndexRazor);
        var resx = Write("Project.Web/Pages/Index.resx");

        Assert.Equal(resx, Locate(razor)!.ResxPath);
    }

    [Fact]
    public void MatchesFileNameCaseInsensitively()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", ProgramWithResourcesPath);
        var razor = Write("Project.Web/Pages/Index.razor", IndexRazor);
        var resx = Write("Project.Web/Resources/Pages/index.resx");

        Assert.Equal(resx, Locate(razor)!.ResxPath);
    }

    [Fact]
    public void FallsBackToResxWithTheSameNameWhenItIsInTheWrongPlace()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", ProgramWithResourcesPath);
        var razor = Write("Project.Web/Pages/Admin/Index.razor", IndexRazor);
        Write("Project.Web/Resources/Index.resx");
        var closer = Write("Project.Web/Resources/Pages/Index.resx");

        var result = Locate(razor);

        Assert.Equal("Project.Web.Resources.Pages.Admin.Index", result!.ExpectedResourceName);
        Assert.Equal(closer, result.ResxPath);
    }

    [Fact]
    public void UsesRootNamespaceFromProjectFile()
    {
        Write("src/Web/Web.csproj", "<Project><PropertyGroup><RootNamespace>Contoso.Web</RootNamespace></PropertyGroup></Project>");
        Write("src/Web/Program.cs", ProgramWithResourcesPath);
        var razor = Write("src/Web/Pages/Index.razor", IndexRazor);
        var resx = Write("src/Web/Resources/Pages/Index.resx");

        var result = Locate(razor);

        Assert.Equal("Contoso.Web.Pages.Index", result!.LocalizerType);
        Assert.Equal(resx, result.ResxPath);
    }

    [Fact]
    public void UsesNamespaceDirectiveFromImports()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", ProgramWithResourcesPath);
        Write("Project.Web/_Imports.razor", "@namespace Other.Name");
        var razor = Write("Project.Web/Pages/Index.razor", IndexRazor);

        Assert.Equal("Other.Name.Pages.Index", Locate(razor)!.LocalizerType);
    }

    [Fact]
    public void FindsLocalizerInjectedInCodeBehind()
    {
        Write("Project.Web/Project.Web.csproj", "<Project />");
        Write("Project.Web/Program.cs", ProgramWithResourcesPath);
        var razor = Write("Project.Web/Pages/Index.razor", "<h1>@L[\"Title\"]</h1>");
        Write("Project.Web/Pages/Index.razor.cs", """
            namespace Project.Web.Pages;
            public partial class Index
            {
                [Inject] private IStringLocalizer<Shared> L { get; set; } = default!;
            }
            """);
        Write("Project.Web/Shared.cs", "namespace Project.Web;\npublic class Shared { }");
        var resx = Write("Project.Web/Resources/Shared.resx");

        var result = Locate(razor, "<h1>@L[\"Title\"]</h1>");

        Assert.Equal("Project.Web.Shared", result!.LocalizerType);
        Assert.Equal(resx, result.ResxPath);
    }

    [Fact]
    public void PicksTheLocalizerUsedOnTheCaretLine()
    {
        var localizers = new List<(string, string)> { ("Index", "Localizer"), ("SharedResource", "SharedLoc") };

        Assert.Equal("SharedResource", ResxLocator.ChooseLocalizer(localizers, "<p>@SharedLoc[\"Save\"]</p>", "Index")!.Value.TypeName);
        Assert.Equal("Index", ResxLocator.ChooseLocalizer(localizers, "<p>@Localizer[\"Save\"]</p>", "Index")!.Value.TypeName);
    }

    [Fact]
    public void PrefersTheComponentsOwnLocalizerWhenTheCaretIsElsewhere()
    {
        var localizers = new List<(string, string)> { ("SharedResource", "SharedLoc"), ("Index", "Localizer") };

        Assert.Equal("Index", ResxLocator.ChooseLocalizer(localizers, "<div>", "Index")!.Value.TypeName);
    }

    [Theory]
    [InlineData("@inject IStringLocalizer<Index> Localizer", "Index", "Localizer")]
    [InlineData("@inject Microsoft.Extensions.Localization.IStringLocalizer<Project.Web.Pages.Index> L", "Project.Web.Pages.Index", "L")]
    [InlineData("@inject IHtmlLocalizer<Index> Html", "Index", "Html")]
    [InlineData("[Inject] public IStringLocalizer<Index>? Loc { get; set; }", "Index", "Loc")]
    public void ParsesLocalizerDeclarations(string text, string type, string name) =>
        Assert.Equal((type, name), ResxLocator.FindLocalizers(text).Single());

    [Theory]
    [InlineData("Index.resx", true)]
    [InlineData("Index.fr.resx", false)]
    [InlineData("Index.en-GB.resx", false)]
    [InlineData("Pages.Index.resx", true)]
    public void DetectsCultureFiles(string file, bool neutral) =>
        Assert.Equal(neutral, ResxLocator.IsNeutral(file));
}
