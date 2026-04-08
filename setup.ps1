# Setup-Skript für Enterprise Zero-Knowledge Password Manager (EZK-PM)
# Ziel-Framework: .NET 10.0

$SolutionName = "EZKPM"
$TargetFramework = "net10.0"

dotnet new sln -n $SolutionName

Write-Host "Erstelle Open-Source Lizenzdatei (AGPL-3.0)..."
$CurrentYear = (Get-Date).Year
$LicenseText = @"
EZK-PM - Enterprise Zero-Knowledge Password Manager
Copyright (C) $CurrentYear Ironclad Vault / EZK-PM Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as
published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.
"@
Set-Content -Path "LICENSE" -Value $LicenseText

Write-Host "Erstelle Projekte für $TargetFramework..."

# Shared Contracts
dotnet new classlib -n "$SolutionName.Shared.Contracts" -f $TargetFramework
dotnet sln add "$SolutionName.Shared.Contracts/$SolutionName.Shared.Contracts.csproj"

# Server PDP
dotnet new webapi -n "$SolutionName.Server.PDP" -f $TargetFramework
dotnet sln add "$SolutionName.Server.PDP/$SolutionName.Server.PDP.csproj"
dotnet add "$SolutionName.Server.PDP/$SolutionName.Server.PDP.csproj" reference "$SolutionName.Shared.Contracts/$SolutionName.Shared.Contracts.csproj"

# Client Core
dotnet new classlib -n "$SolutionName.Client.Core" -f $TargetFramework
dotnet sln add "$SolutionName.Client.Core/$SolutionName.Client.Core.csproj"
dotnet add "$SolutionName.Client.Core/$SolutionName.Client.Core.csproj" reference "$SolutionName.Shared.Contracts/$SolutionName.Shared.Contracts.csproj"

# Client Desktop (Avalonia)
# Hinweis: Hier wird manuell auf net10.0 in der csproj umgestellt, falls das Template noch älter ist.
dotnet new avalonia.app -n "$SolutionName.Client.Desktop"
dotnet sln add "$SolutionName.Client.Desktop/$SolutionName.Client.Desktop.csproj"

# NuGet-Pakete (Versionen für .NET 10 optimiert)
dotnet add "$SolutionName.Client.Core/$SolutionName.Client.Core.csproj" package Portable.BouncyCastle
dotnet add "$SolutionName.Client.Core/$SolutionName.Client.Core.csproj" package Konscious.Security.Cryptography.Argon2
dotnet add "$SolutionName.Client.Core/$SolutionName.Client.Core.csproj" package Fido2NetLib

Write-Host "Initialisiere Git-Repository..."
git init
dotnet new gitignore
git add .

Write-Host "Setup auf .NET 10.0 abgeschlossen."