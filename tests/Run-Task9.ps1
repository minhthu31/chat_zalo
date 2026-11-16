param([string]$ConnectionString = $env:CHAT_TEST_CONNECTION_STRING)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Set CHAT_TEST_CONNECTION_STRING to a migrated PostgreSQL 18 database with _test in its name.'
}
$previousConnection = $env:CHAT_TEST_CONNECTION_STRING
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $env:CHAT_TEST_CONNECTION_STRING = $ConnectionString
    dotnet build backend/Chat.sln
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    dotnet test tests/backend/Chat.Database.Tests/Chat.Database.Tests.csproj --no-build `
        --filter 'FullyQualifiedName~SignalRPostgresTests|FullyQualifiedName~ChatHubSignalRTests|FullyQualifiedName~MessageServiceTests|FullyQualifiedName~BlocksApiTests|FullyQualifiedName~FriendshipsApiTests' `
        --logger 'trx;LogFileName=task9.trx' --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'Task 9 or regression tests failed.' }
}
finally {
    $env:CHAT_TEST_CONNECTION_STRING = $previousConnection
    Pop-Location
}
