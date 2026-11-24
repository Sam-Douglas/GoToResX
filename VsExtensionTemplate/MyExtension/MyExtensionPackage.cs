using MyExtension.Commands;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MyExtension;

/// <summary>
/// The extension's entry point. Loads in the background once Visual Studio has started and registers the menu commands.
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuidString)]
public sealed class MyExtensionPackage : AsyncPackage
{
    private const string PackageGuidString = "0f7ad5b9-c891-48fd-91ac-48bf0413e5ab";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;

        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        if (commandService != null)
            new HelloCommand(this).Register(commandService);
    }
}
