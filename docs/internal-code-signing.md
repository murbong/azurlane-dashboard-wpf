# Internal code signing

This project can use a self-signed Authenticode certificate for internal WPF builds.
This does not replace a public OV/EV code signing certificate for public distribution.

## 1. Create the internal code signing certificate

Run this once on the signing machine:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new_internal_signing_cert.ps1
```

The private key is created in `Cert:\CurrentUser\My`.
The public `.cer` file is exported to `%LOCALAPPDATA%\AzurLaneDashboard\signing` by default.

Keep any exported `.pfx` file outside the repository.

## 2. Trust the public certificate on test machines

Copy the exported `.cer` file to each internal test machine, then import it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install_internal_signing_cert.ps1 -CertificatePath .\azurlane-dashboard-internal-code-signing.cer
```

The default import scope is the current user. For all users on a machine, run an elevated PowerShell session and add `-LocalMachine`.

## 3. Publish and sign

Publish, sign, verify, and ZIP in one step:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish_wpf.ps1 -Clean -Sign -VerifySignature -Zip
```

For the standalone package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build_wpf_standalone.ps1 -Clean -Sign -VerifySignature -Zip
```

The publish script signs the output before it creates the ZIP.
By default the signing script signs `AzurLaneDashboard*.exe` files and `AzurLaneDashboard*.dll` files.
Use `-IncludeAllExe` only if you intentionally want to sign every EXE in the output directory.
Use `-IncludeAllDll` only if you intentionally want to sign every DLL in the output directory.

To sign an existing publish output manually:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\sign_wpf_output.ps1 -Path .\dist -Verify
```

## 4. Verify a signed file

```powershell
signtool verify /pa /v .\dist\AzurLaneDashboard.App.exe
```

You can also check with:

```powershell
Get-AuthenticodeSignature .\dist\AzurLaneDashboard.App.exe
```

The signer certificate must be trusted on the target machine, otherwise Windows can still show trust warnings.
