param(
    [string]$Server = 'BGALT-NAP02',
    [string]$UserName = 'sa',
    [Security.SecureString]$Password
)
$ErrorActionPreference = 'Stop'
if(-not $Password) { $Password = Read-Host 'Development SQL password' -AsSecureString }
$plain = [Net.NetworkCredential]::new('', $Password).Password
$directory = Join-Path $env:APPDATA 'Microsoft/UserSecrets/sparta-architecture-poc'
New-Item -ItemType Directory -Force $directory | Out-Null
$path = Join-Path $directory 'secrets.json'
$secrets = if(Test-Path $path) { Get-Content $path -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
foreach($name in @('Security','Sales','Inventory','Audit')) {
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Server'] = $Server
    $connection['Database'] = "Sparta$name"
    $connection['User ID'] = $UserName
    $connection['Password'] = $plain
    $connection['Encrypt'] = $true
    $connection['TrustServerCertificate'] = $true # Development only; install a trusted certificate for production.
    $connection['MultipleActiveResultSets'] = $true
    $secrets["ConnectionStrings:$name"] = $connection.ConnectionString
}
if(-not $secrets['Authentication:Jwt:IssuerSigningKey']) {
    $secrets['Authentication:Jwt:IssuerSigningKey'] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
}
if(-not $secrets['Seed:Password']) {
    $secrets['Seed:Password'] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) + 'aA1!'
}
$secrets | ConvertTo-Json | Set-Content $path
$plain = $null
Write-Host 'Development secrets saved outside the repository. Existing JWT and seed passwords were preserved.'
