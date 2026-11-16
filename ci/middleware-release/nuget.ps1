# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = Join-Path $root 'artifacts/middleware/nuget'
$consumer = Join-Path $root ('artifacts/middleware/nuget-consumer-' + [guid]::NewGuid().ToString('N'))
$projects = @('src/Signing', 'src/Engine', 'middleware/dotnet', 'middleware/dotnet/AspNetCore', 'middleware/dotnet/Compliance', 'middleware/dotnet/Serilog')
New-Item -ItemType Directory -Force -Path $output, $consumer | Out-Null
foreach ($project in $projects) {
    dotnet restore (Join-Path $root $project) --locked-mode
    if ($LASTEXITCODE) { throw "Restore failed: $project." }
    dotnet pack (Join-Path $root $project) -c Release --no-restore -o $output
    if ($LASTEXITCODE) { throw "Packaging failed: $project." }
}
$version = [IO.File]::ReadAllText((Join-Path $root 'middleware/version.txt')).Trim()
$ids = @('InvenioTech.GateKeeper.Middleware', 'InvenioTech.GateKeeper.AspNetCore', 'InvenioTech.GateKeeper.Compliance.Redaction', 'InvenioTech.GateKeeper.Serilog')
$references = ($ids | ForEach-Object { "<PackageReference Include=`"$_`" Version=`"[$version]`" />" }) -join "`n"
$projectText = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`" />$references</ItemGroup></Project>"
[IO.File]::WriteAllText((Join-Path $consumer 'Consumer.csproj'), $projectText)
$probe = @'
using InvenioTech.GateKeeper.Middleware;
var check = new ConsentCheck(null, diagnostic => { });
if (await check.IsGrantedAsync("synthetic-subject", "example.purpose"))
    throw new System.Exception("The packaged helper must never grant an unknown answer.");
System.Console.WriteLine(typeof(InvenioTech.GateKeeper.AspNetCore.GateKeeperOptions));
System.Console.WriteLine(typeof(InvenioTech.GateKeeper.Compliance.Redaction.GateKeeperRedactor));
System.Console.WriteLine(typeof(InvenioTech.GateKeeper.Serilog.GateKeeperEnricher));
'@
[IO.File]::WriteAllText((Join-Path $consumer 'Program.cs'), $probe)
# Isolate the consumer from repository project references and cached first-party packages.
$feed = [Security.SecurityElement]::Escape($output)
$config = "<configuration><packageSources><clear /><add key=`"local`" value=`"$feed`" /><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`" /></packageSources><packageSourceMapping><clear /><packageSource key=`"local`"><package pattern=`"InvenioTech.GateKeeper.*`" /></packageSource><packageSource key=`"nuget`"><package pattern=`"*`" /></packageSource></packageSourceMapping></configuration>"
[IO.File]::WriteAllText((Join-Path $consumer 'nuget.config'), $config)
$consumerConfig = Join-Path $consumer 'nuget.config'
dotnet restore (Join-Path $consumer 'Consumer.csproj') "-p:RestoreConfigFile=$consumerConfig" --packages (Join-Path $consumer 'packages') -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:ManagePackageVersionsCentrally=false
if ($LASTEXITCODE) { throw 'The packaged dependency closure did not restore.' }
dotnet run --project (Join-Path $consumer 'Consumer.csproj') -c Release --no-restore -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:ManagePackageVersionsCentrally=false
if ($LASTEXITCODE) { throw 'The independent package consumer failed.' }
Write-Output 'NuGet packages restore, compile and execute through a local feed. No publication occurred.'
