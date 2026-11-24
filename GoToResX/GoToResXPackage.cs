using GoToResX.Commands;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GoToResX;

/// <summary>
/// The extension's entry point. Loads in the background once Visual Studio has started and registers the menu commands.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuidString)]
public sealed class GoToResXPackage : AsyncPackage
{
    private const string PackageGuidString = "48679a5e-de31-41b8-a8f0-f6f767c0aece";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;
        var componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
        var textManager = await GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (commandService != null && componentModel != null && textManager != null)
            new GoToResxCommand(this, componentModel, textManager).Register(commandService);
    }
}
