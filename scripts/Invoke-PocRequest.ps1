param(
    [string]$UserName = 'sales.reader',
    [string]$Path = '/api/odata/SalesOrder',
    [string]$Method = 'GET',
    [string]$Body,
    [string]$BaseUrl = 'http://localhost:5180'
)
$ErrorActionPreference = 'Stop'
$secretPath = Join-Path $env:APPDATA 'Microsoft/UserSecrets/sparta-architecture-poc/secrets.json'
$secrets = Get-Content $secretPath -Raw | ConvertFrom-Json -AsHashtable
$credentials = @{ userName = $UserName; password = $secrets['Seed:Password'] } | ConvertTo-Json
$token = Invoke-RestMethod "$BaseUrl/api/Authentication/Authenticate" -Method Post -ContentType 'application/json' -Body $credentials
$arguments = @{ Uri = "$BaseUrl$Path"; Method = $Method; Headers = @{ Authorization = "Bearer $token" } }
if($Body) { $arguments.Body = $Body; $arguments.ContentType = 'application/json' }
Invoke-RestMethod @arguments
