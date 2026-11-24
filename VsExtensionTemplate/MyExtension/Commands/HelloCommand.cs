using MyExtension.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;

namespace MyExtension.Commands;

/// <summary>
/// Extensions > My Extension > Say Hello. The ids match <c>MyExtension.vsct</c>.
/// </summary>
internal sealed class HelloCommand(AsyncPackage package)
{
    public static readonly Guid CommandSet = new("cdb7a5cc-f1d9-49d9-b316-4e935e683a98");

    public const int HelloCommandId = 0x0100;

    public void Register(IMenuCommandService commandService)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        commandService.AddCommand(new MenuCommand((_, _) => Execute(), new CommandID(CommandSet, HelloCommandId)));
    }

    private void Execute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        VsShellUtilities.ShowMessageBox(
            package,
            Greeting.For(Environment.UserName),
            "My Extension",
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
