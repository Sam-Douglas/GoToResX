using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GoToResX.Core;

/// <summary>The outcome of ResxLocator.Locate.</summary>
/// <param name="ResxPath">The neutral .resx file to open, or null if none was found.</param>
/// <param name="LocalizerType">The full name of the localizer's T, as far as it could be resolved.</param>
/// <param name="ExpectedResourceName">The resource name the runtime looks for, e.g. Project.Web.Resources.Pages.Index.</param>
public sealed class ResxLocation(string? resxPath, string localizerType, string expectedResourceName)
{
    public string? ResxPath { get; } = resxPath;
    public string LocalizerType { get; } = localizerType;
    public string ExpectedResourceName { get; } = expectedResourceName;
}

/// <summary>
/// Finds the .resx file that a Razor component's string localizer reads from.
/// </summary>
/// <remarks>
/// Follows the runtime's rules (ResourceManagerStringLocalizerFactory): the resource name is the project's root
/// namespace, then ResourcesPath, then T's full name with the root namespace removed. MSBuild names each
/// embedded .resx after the root namespace plus its folder path, so the two can be matched directly.
/// </remarks>
public static class ResxLocator
{
    private static readonly Regex LocalizerDeclaration = new(
        @"\bI(?:String|Html)Localizer\s*<\s*(?:global::)?(?<type>[\w.]+)\s*>\??\s+@?(?<name>\w+)",
        RegexOptions.Compiled);

    private static readonly Regex RazorNamespaceDirective = new(@"^\s*@namespace\s+(?<ns>[\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex CSharpNamespace = new(@"^\s*namespace\s+(?<ns>[\w.]+)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ResourcesPathSetting = new(@"\bResourcesPath\s*=\s*""(?<path>[^""]*)""", RegexOptions.Compiled);
    private static readonly Regex RootNamespaceProperty = new(@"<RootNamespace>\s*(?<ns>[^<]+?)\s*</RootNamespace>", RegexOptions.Compiled);
    private static readonly Regex AssemblyNameProperty = new(@"<AssemblyName>\s*(?<ns>[^<]+?)\s*</AssemblyName>", RegexOptions.Compiled);

    private static readonly Regex CultureSuffix = new(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9]{2,8})*$", RegexOptions.Compiled);

    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".vs", ".git"];

    /// <summary>
    /// Finds the .resx file for the localizer used in a Razor component.
    /// </summary>
    /// <param name="razorFilePath">Full path of the .razor file.</param>
    /// <param name="razorText">The file's current text (may have unsaved edits).</param>
    /// <param name="caretLineText">The line the caret is on, used to pick a localizer when the component has several.</param>
    /// <returns>Null if the file isn't inside a project.</returns>
    public static ResxLocation? Locate(string razorFilePath, string razorText, string? caretLineText)
    {
        var projectFile = FindProjectFile(razorFilePath);
        if (projectFile is null)
            return null;

        var projectDir = Path.GetDirectoryName(projectFile)!;
        var rootNamespace = GetRootNamespace(projectFile);
        var files = EnumerateProjectFiles(projectDir).ToList();

        var componentName = ComponentName(razorFilePath);
        var codeBehindPath = razorFilePath + ".cs";
        var codeBehindText = File.Exists(codeBehindPath) ? File.ReadAllText(codeBehindPath) : null;

        var localizers = FindLocalizers(razorText)
            .Concat(codeBehindText is null ? [] : FindLocalizers(codeBehindText))
            .Concat(ImportsFiles(razorFilePath, projectDir).SelectMany(path => FindLocalizers(File.ReadAllText(path))))
            .ToList();

        // A component with no localizer of its own: assume the one it would use is IStringLocalizer<ThisComponent>.
        var typeName = ChooseLocalizer(localizers, caretLineText, componentName)?.TypeName ?? componentName;
        var fullTypeName = ResolveTypeName(typeName, componentName, razorFilePath, razorText, codeBehindText, projectDir, rootNamespace, files);

        var resourcesPath = FindResourcesPath(files);
        var expected = ExpectedResourceName(fullTypeName, rootNamespace, resourcesPath);

        var resxFiles = files.Where(f => f.EndsWith(".resx", StringComparison.OrdinalIgnoreCase) && IsNeutral(f)).ToList();

        var match = resxFiles.FirstOrDefault(f => string.Equals(ManifestName(f, projectDir, rootNamespace), expected, StringComparison.OrdinalIgnoreCase))
            ?? ClosestByName(resxFiles, SimpleName(fullTypeName), expected, f => ManifestName(f, projectDir, rootNamespace));

        return new ResxLocation(match, fullTypeName, expected);
    }

    /// <summary>Every IStringLocalizer or IHtmlLocalizer declared in the text, as its type argument and variable name.</summary>
    public static IEnumerable<(string TypeName, string VariableName)> FindLocalizers(string text) =>
        LocalizerDeclaration.Matches(text).Cast<Match>().Select(m => (m.Groups["type"].Value, m.Groups["name"].Value));

    /// <summary>
    /// The localizer used on the caret's line; otherwise the one whose type argument is the component itself;
    /// otherwise the first one declared.
    /// </summary>
    public static (string TypeName, string VariableName)? ChooseLocalizer(
        IReadOnlyList<(string TypeName, string VariableName)> localizers, string? caretLineText, string componentName)
    {
        if (localizers.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(caretLineText))
        {
            foreach (var localizer in localizers)
            {
                if (Regex.IsMatch(caretLineText, $@"(?<![\w.])@?{Regex.Escape(localizer.VariableName)}\s*\["))
                    return localizer;
            }
        }

        foreach (var localizer in localizers)
        {
            if (SimpleName(localizer.TypeName) == componentName)
                return localizer;
        }

        return localizers[0];
    }

    /// <summary>The resource name the runtime looks up for fullTypeName.</summary>
    public static string ExpectedResourceName(string fullTypeName, string rootNamespace, string? resourcesPath)
    {
        var relative = fullTypeName.StartsWith(rootNamespace + ".", StringComparison.Ordinal)
            ? fullTypeName.Substring(rootNamespace.Length + 1)
            : fullTypeName;

        var prefix = string.IsNullOrWhiteSpace(resourcesPath)
            ? rootNamespace
            : rootNamespace + "." + resourcesPath!.Trim('/', '\\', '.').Replace('/', '.').Replace('\\', '.');

        return prefix + "." + relative;
    }

    /// <summary>The manifest resource name MSBuild gives a .resx file: root namespace plus its path, with dots.</summary>
    public static string ManifestName(string resxPath, string projectDir, string rootNamespace)
    {
        var relative = MakeRelative(projectDir, resxPath);
        relative = relative.Substring(0, relative.Length - ".resx".Length);
        var dotted = string.Join(".", relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(SanitizeIdentifier));
        return rootNamespace + "." + dotted;
    }

    private static string ResolveTypeName(
        string typeName, string componentName, string razorFilePath, string razorText, string? codeBehindText,
        string projectDir, string rootNamespace, IReadOnlyList<string> files)
    {
        // A dotted name is treated as already qualified.
        if (typeName.Contains('.'))
            return typeName;

        if (typeName == componentName)
            return ComponentNamespace(razorFilePath, razorText, codeBehindText, projectDir, rootNamespace) + "." + typeName;

        // Another component or class in the project with that name.
        var razor = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), typeName + ".razor", StringComparison.OrdinalIgnoreCase));
        if (razor is not null)
        {
            var behind = razor + ".cs";
            return ComponentNamespace(razor, File.ReadAllText(razor), File.Exists(behind) ? File.ReadAllText(behind) : null, projectDir, rootNamespace) + "." + typeName;
        }

        var cs = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), typeName + ".cs", StringComparison.OrdinalIgnoreCase));
        if (cs is not null)
        {
            var ns = CSharpNamespace.Match(File.ReadAllText(cs));
            if (ns.Success)
                return ns.Groups["ns"].Value + "." + typeName;
        }

        return rootNamespace + "." + typeName;
    }

    /// <summary>
    /// A component's namespace: its own @namespace, else its code-behind's namespace, else the nearest
    /// _Imports.razor @namespace plus the folders below it, else the root namespace plus its folders.
    /// </summary>
    public static string ComponentNamespace(string razorFilePath, string razorText, string? codeBehindText, string projectDir, string rootNamespace)
    {
        var own = RazorNamespaceDirective.Match(razorText);
        if (own.Success)
            return own.Groups["ns"].Value;

        if (codeBehindText is not null)
        {
            var behind = CSharpNamespace.Match(codeBehindText);
            if (behind.Success)
                return behind.Groups["ns"].Value;
        }

        var folder = Path.GetDirectoryName(razorFilePath)!;
        foreach (var imports in ImportsFiles(razorFilePath, projectDir))
        {
            var directive = RazorNamespaceDirective.Match(File.ReadAllText(imports));
            if (directive.Success)
                return Combine(directive.Groups["ns"].Value, NamespaceSuffix(Path.GetDirectoryName(imports)!, folder));
        }

        return Combine(rootNamespace, NamespaceSuffix(projectDir, folder));
    }

    /// <summary>_Imports.razor files from the component's folder up to the project folder, nearest first.</summary>
    private static IEnumerable<string> ImportsFiles(string razorFilePath, string projectDir)
    {
        var root = Path.GetFullPath(projectDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetDirectoryName(Path.GetFullPath(razorFilePath));

        while (dir is not null && dir.Length >= root.Length)
        {
            var imports = Path.Combine(dir, "_Imports.razor");
            if (File.Exists(imports) && !string.Equals(Path.GetFullPath(imports), Path.GetFullPath(razorFilePath), StringComparison.OrdinalIgnoreCase))
                yield return imports;

            dir = Path.GetDirectoryName(dir);
        }
    }

    private static string? FindProjectFile(string filePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (dir is not null)
        {
            var project = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
            if (project is not null)
                return project;

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    /// <summary>The project's RootNamespace property, else its AssemblyName property, else the project file's name.</summary>
    public static string GetRootNamespace(string projectFile)
    {
        var text = File.ReadAllText(projectFile);
        var match = RootNamespaceProperty.Match(text);
        if (!match.Success)
            match = AssemblyNameProperty.Match(text);

        var name = match.Success && !match.Groups["ns"].Value.Contains("$(")
            ? match.Groups["ns"].Value
            : Path.GetFileNameWithoutExtension(projectFile);

        return string.Join(".", name.Split('.').Select(SanitizeIdentifier));
    }

    private static IEnumerable<string> EnumerateProjectFiles(string projectDir)
    {
        var pending = new Stack<string>();
        pending.Push(projectDir);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFiles(dir).ToList(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in entries)
                yield return file;

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;

                // A nested project's files belong to that project.
                if (Directory.EnumerateFiles(sub, "*.csproj").Any())
                    continue;

                pending.Push(sub);
            }
        }
    }

    /// <summary>The ResourcesPath set in AddLocalization(...), read from Program.cs or Startup.cs.</summary>
    private static string? FindResourcesPath(IEnumerable<string> files)
    {
        foreach (var file in files.Where(f => Path.GetFileName(f) is "Program.cs" or "Startup.cs"))
        {
            var match = ResourcesPathSetting.Match(File.ReadAllText(file));
            if (match.Success)
                return match.Groups["path"].Value;
        }

        return null;
    }

    /// <summary>True for Index.resx, false for culture files such as Index.fr.resx or Index.en-GB.resx.</summary>
    public static bool IsNeutral(string resxPath)
    {
        var name = Path.GetFileNameWithoutExtension(resxPath);
        var dot = name.LastIndexOf('.');
        if (dot < 0)
            return true;

        var suffix = name.Substring(dot + 1);
        if (!CultureSuffix.IsMatch(suffix))
            return true;

        try
        {
            var culture = CultureInfo.GetCultureInfo(suffix);
            // .NET (ICU) accepts any well-formed name as a custom culture; a real one has a display name other than its tag.
            return culture.Equals(CultureInfo.InvariantCulture)
                || (culture.CultureTypes & CultureTypes.UserCustomCulture) != 0
                || string.Equals(culture.EnglishName, suffix, StringComparison.OrdinalIgnoreCase)
                || culture.EnglishName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return true;
        }
    }

    /// <summary>
    /// The neutral .resx named after the type whose resource name shares the most leading parts with the expected one,
    /// e.g. Resources/Pages/Index.resx over Resources/Index.resx for Resources.Pages.Admin.Index.
    /// </summary>
    private static string? ClosestByName(IEnumerable<string> resxFiles, string simpleName, string expected, Func<string, string> manifestName)
    {
        var expectedParts = expected.Split('.');

        return resxFiles
            .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), simpleName, StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileNameWithoutExtension(f).EndsWith("." + simpleName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => SharedLeadingParts(manifestName(f).Split('.'), expectedParts))
            .ThenBy(f => f.Length)
            .FirstOrDefault();
    }

    private static int SharedLeadingParts(string[] x, string[] y)
    {
        var i = 0;
        while (i < x.Length && i < y.Length && string.Equals(x[i], y[i], StringComparison.OrdinalIgnoreCase))
            i++;
        return i;
    }

    private static string ComponentName(string razorFilePath) => Path.GetFileNameWithoutExtension(razorFilePath);

    private static string SimpleName(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName.Substring(dot + 1);
    }

    private static string NamespaceSuffix(string baseDir, string dir)
    {
        var relative = MakeRelative(baseDir, dir);
        return string.Join(".", relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(SanitizeIdentifier));
    }

    private static string Combine(string ns, string suffix) => suffix.Length == 0 ? ns : ns + "." + suffix;

    private static string MakeRelative(string baseDir, string path)
    {
        var root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full);
    }

    /// <summary>Folder names become namespace parts the way MSBuild and Razor do it: invalid characters become underscores.</summary>
    private static string SanitizeIdentifier(string part)
    {
        var chars = part.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_').ToArray();
        var result = new string(chars);
        return result.Length > 0 && char.IsDigit(result[0]) ? "_" + result : result;
    }
}
