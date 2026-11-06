param(
    [string]$ConnectionString = $env:CHAT_TEST_CONNECTION_STRING
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Set CHAT_TEST_CONNECTION_STRING to a migrated, dedicated PostgreSQL database whose name contains _test.'
}

$task9Root = Split-Path -Parent $PSScriptRoot
$previousConnection = $env:CHAT_TEST_CONNECTION_STRING
Push-Location $task9Root
try {
    $env:CHAT_TEST_CONNECTION_STRING = $ConnectionString
    dotnet build backend/Chat.sln
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    dotnet build frontend/Chat.Client.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    dotnet test tests/backend/Chat.Database.Tests/Chat.Database.Tests.csproj --no-build `
        --filter 'FullyQualifiedName~SignalR|FullyQualifiedName~ConversationNotificationsTests|FullyQualifiedName~BlocksApiTests|FullyQualifiedName~FriendshipsApiTests|FullyQualifiedName~ConversationsHttpPostgresTests' `
        --logger 'trx;LogFileName=task9.trx' --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'Task 9 or regression tests failed.' }
}
finally {
    $env:CHAT_TEST_CONNECTION_STRING = $previousConnection
    Pop-Location
}
