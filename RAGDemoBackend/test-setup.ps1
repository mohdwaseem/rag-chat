# Test Script for RAG Demo Backend

Write-Host "=================================" -ForegroundColor Cyan
Write-Host "RAG Demo Backend - Setup Checker" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Check Docker
Write-Host "1. Checking Docker..." -NoNewline
try {
    $dockerVersion = docker --version 2>$null
    if ($dockerVersion) {
        Write-Host " OK - $dockerVersion" -ForegroundColor Green
    } else {
        Write-Host " FAILED - Docker not found" -ForegroundColor Red
    }
} catch {
    Write-Host " FAILED - Docker not found" -ForegroundColor Red
}

# Check if Qdrant is running
Write-Host "2. Checking Qdrant..." -NoNewline
try {
    $qdrantStatus = docker ps --filter "name=qdrant" --format "{{.Status}}" 2>$null
    if ($qdrantStatus -like "*Up*") {
        Write-Host " OK - Running" -ForegroundColor Green
    } else {
        Write-Host " WARNING - Not running" -ForegroundColor Yellow
        Write-Host "   Start with: docker run -d -p 6333:6333 -p 6334:6334 --name qdrant qdrant/qdrant" -ForegroundColor Gray
    }
} catch {
    Write-Host " WARNING - Not running" -ForegroundColor Yellow
}

# Check .NET SDK
Write-Host "3. Checking .NET SDK..." -NoNewline
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($dotnetVersion) {
        Write-Host " OK - Version $dotnetVersion" -ForegroundColor Green
    } else {
        Write-Host " FAILED - .NET SDK not found" -ForegroundColor Red
    }
} catch {
    Write-Host " FAILED - .NET SDK not found" -ForegroundColor Red
}

# Check if project builds
Write-Host "4. Checking project build..." -NoNewline
try {
    $buildResult = dotnet build --nologo --verbosity quiet 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK - Build successful" -ForegroundColor Green
    } else {
        Write-Host " FAILED - Build failed" -ForegroundColor Red
    }
} catch {
    Write-Host " FAILED - Build check failed" -ForegroundColor Red
}

# Check required folders
Write-Host "5. Checking folders..." -NoNewline
$folders = @("Data\SampleDocuments", "Models")
$allExist = $true
foreach ($folder in $folders) {
    if (-not (Test-Path $folder)) {
        $allExist = $false
    }
}
if ($allExist) {
    Write-Host " OK - All folders exist" -ForegroundColor Green
} else {
    Write-Host " WARNING - Creating folders..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path "Data\SampleDocuments" | Out-Null
    New-Item -ItemType Directory -Force -Path "Models" | Out-Null
    Write-Host "   OK - Folders created" -ForegroundColor Green
}

Write-Host ""
Write-Host "=================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

if ($qdrantStatus -notlike "*Up*") {
    Write-Host "Start Qdrant:" -ForegroundColor Yellow
    Write-Host "  docker run -d -p 6333:6333 -p 6334:6334 --name qdrant qdrant/qdrant" -ForegroundColor White
    Write-Host ""
}

Write-Host "Run the application:" -ForegroundColor Green
Write-Host "  dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "Open Swagger UI:" -ForegroundColor Green
Write-Host "  https://localhost:5001/swagger" -ForegroundColor White
Write-Host ""
