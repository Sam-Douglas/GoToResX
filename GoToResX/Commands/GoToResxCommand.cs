using GoToResX.Core;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;

namespace GoToResX.Commands;

/// <summary>
/// Right-click in a .razor file > Go To ResX. Opens the .resx file the component's localizer reads from.
/// The ids match GoToResX.vsct.
/// </summary>
internal sealed class GoToResxCommand(AsyncPackage package, IComponentModel componentModel, IVsTextManager textManager)
{
    public static readonly Guid CommandSet = new("a3cf9f65-46f9-44ce-9e38-89d36ab90165");

    public const int GoToResxCommandId = 0x0100;

    private const string Title = "Go To ResX";

    private readonly IVsEditorAdaptersFactoryService _adapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();

    public void Register(IMenuCommandService commandService)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var command = new OleMenuCommand((_, _) => Execute(), new CommandID(CommandSet, GoToResxCommandId));
        command.BeforeQueryStatus += (_, _) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            command.Visible = command.Enabled = GetActiveRazorDocument() is not null;
        };
        commandService.AddCommand(command);
    }

    private void Execute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (GetActiveRazorDocument() is not { } active)
            return;

        package.JoinableTaskFactory.RunAsync(async () =>
        {
            ResxLocation? location;
            try
            {
                // Reads the project's files from disk, so keep it off the UI thread.
                location = await Task.Run(() => ResxLocator.Locate(active.Path, active.Text, active.CaretLine));
            }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowMessage($"Couldn't look for the resource file:\n\n{ex.Message}", OLEMSGICON.OLEMSGICON_CRITICAL);
                return;
            }

            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (location is null)
                ShowMessage($"{Path.GetFileName(active.Path)} isn't inside a project (no .csproj found above it).", OLEMSGICON.OLEMSGICON_WARNING);
            else if (location.ResxPath is null)
                ShowMessage($"No resource file found for {location.LocalizerType}.\n\nThe runtime looks for the resource {location.ExpectedResourceName}.", OLEMSGICON.OLEMSGICON_WARNING);
            else
                VsShellUtilities.OpenDocument(package, location.ResxPath);
        }).FileAndForget("GoToResX/GoToResx");
    }

    /// <summary>The active editor's file, text and caret line, if it's a .razor file.</summary>
    private (string Path, string Text, string CaretLine)? GetActiveRazorDocument()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (textManager.GetActiveView(1, null, out var vsTextView) != 0 || vsTextView is null)
            return null;

        if (_adapters.GetWpfTextView(vsTextView) is not IWpfTextView view)
            return null;

        var buffer = view.TextDataModel.DocumentBuffer;
        if (!buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
            return null;

        if (!document.FilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            return null;

        var caret = view.Caret.Position.BufferPosition;
        var caretLine = caret.Snapshot.TextBuffer == buffer
            ? caret.GetContainingLine().GetText()
            : view.TextSnapshot.GetLineFromPosition(caret.Position).GetText();

        return (document.FilePath, buffer.CurrentSnapshot.GetText(), caretLine);
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(package, message, Title, icon, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
