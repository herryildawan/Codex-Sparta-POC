$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet tool restore
    if($LASTEXITCODE) { throw 'Tool restore failed.' }
    dotnet build Sparta.sln -c Release
    if($LASTEXITCODE) { throw 'Build failed.' }
    dotnet run --project src/Sparta.Api -c Release --no-build -- --migrate
    if($LASTEXITCODE) { throw 'Migration failed.' }
    dotnet run --project src/Sparta.Api -c Release --no-build -- --seed
    if($LASTEXITCODE) { throw 'Seeding failed.' }
    dotnet run --project tests/Sparta.IntegrationTests -c Release --no-build
    if($LASTEXITCODE) { throw 'Integration checks failed.' }
} finally { Pop-Location }
