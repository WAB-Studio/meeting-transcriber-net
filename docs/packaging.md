# Packaging

Building, signing and handing out the MSIX. `arquitectura.md` §11 is the design this serves —
*MSIX firmado ──sideload──► alpha* — and this is the command that makes its first row real.

**Everything up to and including §3 has been run in this repository and is written from what it
did. §4 has not been run by anybody**, and that is not an oversight: running it needs a machine
that did not build the package, which is #151. Read §4 as the best account there is and #4's own
warning as the reason to read it carefully.

## What this produces

One signed `.msix` — `MeetingTranscriber.App_<version>_x64.msix` — holding an x64 build of the
application with **the .NET runtime and the fonts inside it**. It installs per user, with no
administrator rights, on a machine that already trusts the certificate.

**The Windows App SDK is not inside it.** The package's manifest declares

```xml
<PackageDependency Name="Microsoft.WindowsAppRuntime.2" MinVersion="2.3.1.0" ... />
```

which is a *framework* dependency: Windows resolves it at install time from a package that has to
already be registered on the machine. That is why the build writes
`AppPackages/<name>_<version>_x64_Test/Dependencies/{x64,x86,arm64,win32}/Microsoft.WindowsAppRuntime.2.msix`
beside the package, and why §4 hands over a folder rather than a file.
`PackagedAppTests.The_package_needs_the_windows_app_runtime_from_outside_itself` is what keeps this
section honest — the list is asserted, so a dependency arriving or leaving reddens rather than
silently changing what somebody has to be handed.

Updates during the alpha are manual. There is no update channel and no version check: build a
package whose `<Identity Version>` in `src/MeetingTranscriber.App/Package.appxmanifest` is higher
than the one installed, and `Add-AppxPackage` it over the top.

## 1 — The certificate, once per machine, once a year

A self-signed code-signing certificate whose subject is **exactly** the manifest's `Publisher`.

```powershell
$cert = New-SelfSignedCertificate `
  -Type CodeSigningCert `
  -Subject "CN=pc" `
  -KeyUsage DigitalSignature `
  -FriendlyName "MeetingTranscriber alpha" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
```

**`-Subject` must match `<Identity Publisher>` in `src/MeetingTranscriber.App/Package.appxmanifest`
character for character — `CN=pc` today.** Signing with any other subject produces a package
Windows refuses for a publisher mismatch, and the message does not say which of the two is wrong.
That is the hour this line saves.

**`New-SelfSignedCertificate` defaults to one year.** Packages already installed keep running past
it, but signing a new one stops working, and the error a year from now reads as an unrelated signing
problem. Either pass `-NotAfter (Get-Date).AddYears(5)` or expect to make another certificate — and
note that a package signed by a *different* certificate needs its `.cer` trusted again on every
machine that has one installed.

A machine may already carry a `CN=pc` certificate that is not this one. This repository's own
machine had an Encrypting File System certificate under that subject, which the packaging build will
not sign with. Check the usage, not the subject:

```powershell
Get-ChildItem Cert:\CurrentUser\My |
  Where-Object { $_.Subject -eq "CN=pc" } |
  Select-Object Thumbprint, FriendlyName, EnhancedKeyUsageList
```

The one you want reads `Code Signing`.

## 2 — Exporting the two files, and which one is which

**Export outside the checkout.** A code-signing private key in the working tree is one `git add -f`
from being published and one `git clean -xdf` from being destroyed, and neither is a risk worth
taking to save a path.

```powershell
$keys = "$env:USERPROFILE\MeetingTranscriber.keys"
New-Item -ItemType Directory -Force $keys | Out-Null
$password = ConvertTo-SecureString -String "<a password you choose>" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "$keys\MeetingTranscriber.pfx" -Password $password
Export-Certificate  -Cert $cert -FilePath "$keys\MeetingTranscriber.cer"
```

- **The `.pfx` holds the private key.** It is what signs, and it is never committed and never handed
  to anybody.
- **The `.cer` is the public certificate.** It is what a recipient trusts, and it is the only one of
  the two that is ever handed over. The packaging build drops a copy of it beside the package.

`.gitignore` carries `*.pfx` and `*.cer` as a backstop for the accident, not as the plan.

The password is typed and never stored: no file in this tree holds it and this document suggests no
default. It does go on a command line in §3, so it is in that shell's history and readable in the
process list while the build runs — which is the cost of the simplest thing that works, and worth
knowing rather than worth hiding.

## 3 — The packaging build

One command, from the repository root:

```powershell
dotnet publish src/MeetingTranscriber.App/MeetingTranscriber.App.csproj `
  -c Release `
  -p:Platform=x64 `
  -p:PublishProfile=win-x64 `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateKeyFile=$keys\MeetingTranscriber.pfx `
  -p:PackageCertificatePassword=<the password>
```

**`dotnet publish` is what produced the package here**, on 2026-09-11, with the Windows SDK MSIX
build tools that come in through the Windows App SDK package — no Developer PowerShell, no
`msbuild`, nothing installed by hand. The single-project MSIX targets have historically wanted full
MSBuild, so `msbuild /t:Publish` with the same `-p:` switches is what to reach for if this stops
working; it was not needed and has not been run in this repository. If you ever have to use it,
change this paragraph rather than leaving both here as equally good.

The package lands under `src/MeetingTranscriber.App/AppPackages/`, in a folder named for the version
and architecture, beside the `.cer`, an `Install.ps1`, `Add-AppDevPackage.ps1` and `Dependencies/`.
`.gitignore` already ignores `AppPackages/` and `*.msix`.

Three warnings come out of that command and all three are expected:

- **APPX0105** — *cannot import the key file, it may be password protected* — and **APPX0107** —
  *the certificate specified is not valid for signing*. Both come from a validation pass that reads
  the `.pfx` without the password; the signing itself then succeeds with it. The package that came
  out of the run above is signed. To see that for yourself rather than take it on trust:

  ```powershell
  (Get-AuthenticodeSignature .\src\MeetingTranscriber.App\AppPackages\*\*.msix).SignerCertificate |
    Select-Object Subject, Thumbprint
  ```

  which should read `CN=pc` and your thumbprint. `Status` reads `UnknownError` until the certificate
  is trusted, which is §4 and is a fact about *this* machine's stores rather than about the package.
- **`mspdbcmf.exe` could not be found. A symbols package will not be generated.** A `.msixsym` is
  for uploading to the Store's symbol service. Nothing here uploads anything.

### Then prove the package, in the same breath

```powershell
$env:MEETING_TRANSCRIBER_EXPECT_PACKAGE = 1
dotnet test --no-build
```

`PackagedAppTests` is the only thing in this repository that reads a built package, and it **skips**
when there is none — which is right for CI, where the packaging build never runs, and dangerous for
you, because a skipped test and a passing test read the same in a summary line. That variable turns
the absence of a package into a failure, so the run either proves the package or says it could not
find one. Set it every time; it is the second half of this command and not an option.

Note what those facts do **not** cover: they compare the package against
`Package.appxmanifest`, so a package built before a C# or XAML change still passes. Build and prove
in one sitting.

### What a green `dotnet build` does not tell you

`dotnet build` over the solution builds the application and produces **no** `.msix` at all. The
package appears only under `GenerateAppxPackageOnBuild=true`, and only from a Release publish, for
the self-contained layout to be what goes inside it.

The second thing to watch is the identity. A Debug build on a checkout that has a
`PackageIdentity.props` writes a suffixed identity into the generated manifest — see the argument in
`MeetingTranscriber.App.csproj` and `docs/ui-probe.md`. A Release build never does, because that
import is conditioned on `Configuration == Debug`. **If the packaged manifest comes back with a
suffixed `Identity Name`, the publish was not a Release one.**

## 4 — Handing it over — not run from here

Everything below describes the receiving machine. **Nobody has done it**; #151 is the card that
does, and the first person to follow this should expect to correct it.

**Hand over the whole `<name>_<version>_x64_Test` folder**, not the `.msix` alone. The Windows App
SDK is a framework dependency (see *What this produces*), so a bare package fails with
`0x80073CF3` — *this app package's dependencies could not be resolved* — on any machine that does
not already have `Microsoft.WindowsAppRuntime.2`.

```powershell
# Administrator. Trusting the certificate is the only step that needs it.
Import-Certificate -FilePath MeetingTranscriber.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# Not administrator.
Add-AppxPackage -Path MeetingTranscriber.App_<version>_x64.msix `
  -DependencyPath Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix
```

`Install.ps1` in the same folder does both and resolves the dependency itself; prefer it if it
works, and keep the two commands for when it does not say why.

**Do not run this on a development machine that drives the UI probe.** A Release package carries
`<Identity Name>` `7feb8c95-4553-46f0-a036-6574f4cd7cb4` — the same name a checkout without a
`PackageIdentity.props` registers under. Installing here replaces that registration, moves the
install location out of the checkout, and every probe verb then refuses because `docs/ui-probe.md`'s
own check no longer finds the application under the working directory. `docs/ui-probe.md` says how
to register it back.

**Smart App Control is on in a clean Windows 11 install, and it can refuse this package whatever the
certificate says.** It judges reputation, not signatures, and a publisher with none has nothing to
judge. Turning it off is one-way: it does not turn back on without reinstalling Windows. That is the
alpha's accepted cost and it is #79's own decision — the answer is not to fight Smart App Control
but to hand the package to people who will take that trade.

Once it is installed, `PackagingChecksWindow` in the application answers the two questions this
document cannot: whether it is really running packaged, and from where. Its own remark says it goes
when those two stop being needed, and #151 is where that gets decided.
