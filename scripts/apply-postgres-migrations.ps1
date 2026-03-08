param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"

$migrationFiles = Get-ChildItem -Path "infra/postgres/migrations" -Filter "*.sql" | Sort-Object Name

foreach ($file in $migrationFiles) {
    Write-Host "Applying migration $($file.Name)..."
    & $PsqlPath $ConnectionString -v ON_ERROR_STOP=1 -f $file.FullName
}

Write-Host "All migrations applied."
