# My Extension

A short description of what My Extension does.

## Commands

Find these under **Extensions > My Extension**.

| Command | What it does |
|---|---|
| **Say Hello** | Shows a greeting. Replace this with your own commands. |

<!-- TEMPLATE: delete everything from here down once you've set up your extension. -->

---

## Using this template

A minimal Visual Studio extension using the **SDK-style VSIX project** format (Visual Studio 18.5+),
with a GitHub Actions workflow that builds, tests and publishes it.

### What's included

| Path | What it is |
|---|---|
| `MyExtension/` | The VSIX project: an `AsyncPackage` that loads in the background, one menu command (`.vsct`) and the manifest. |
| `MyExtension.Core/` | A `netstandard2.0` library for logic that doesn't touch Visual Studio APIs, so it can be unit tested. |
| `MyExtension.UnitTests/` | xUnit tests for `MyExtension.Core` on .NET 10. |
| `.github/workflows/ci-cd.yaml` | Builds and tests every push and PR. Running it manually publishes to the Open VSIX Gallery and the Marketplace and creates a GitHub release. |
| `vs-publish.json` | Marketplace listing details. The README becomes the Marketplace overview. |
| `Initialize-Extension.ps1` | One-off script that renames everything and makes new GUIDs. |

### Set up

1. Create a repository from this template and clone it.
2. Run the setup script from the repository root:

   ```powershell
   ./Initialize-Extension.ps1 -Name MyCoolExtension -DisplayName "My Cool Extension" -Publisher "Your Name" -GitHubRepo your-account/MyCoolExtension
   ```

3. Fill in the description and tags in `source.extension.vsixmanifest`, and `publisher`, `categories` and `tags` in `vs-publish.json`.
4. Replace `Resources/Icon.png` (128×128) and update the copyright line in `LICENSE.txt` if needed.
5. Open the `.slnx` and press **F5**. The extension deploys into the Visual Studio Experimental Instance.

### Publishing

1. Create a publisher on the [Visual Studio Marketplace](https://marketplace.visualstudio.com/manage) and put its ID in `vs-publish.json`.
2. Add an Azure DevOps personal access token with **Marketplace (Publish)** scope as the `VS_PUBLISHER_ACCESS_TOKEN` repository secret.
3. Run **Actions > Build and Publish > Run workflow**. The version is stamped automatically from the run number.

To make PRs require a green build, set **Status Check** as the required check in branch protection.

### Requirements

- Visual Studio 18.5 or later with the **Visual Studio extension development** workload.
- .NET 10 SDK for the unit tests.
- The extension itself installs into Visual Studio 2022 (17.14+) and later, on x64 and Arm64.

### Notes

- Add `<UseWpf>true</UseWpf>` to the VSIX project if you add XAML.
- MEF components (editor extensions, `[Export]` classes) work as-is: the manifest already declares a `MefComponent` asset.
- Add an options page with `[ProvideOptionPage]` and a `DialogPage` subclass. The Inline Git Blame extension shows how.
