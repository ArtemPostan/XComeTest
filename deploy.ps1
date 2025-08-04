# Папка с проектом
$buildDir = "C:\UnityGames\XComeTest"

# Локальный клон вашего репозитория
$repoDir  = "C:\UnityGames\GitRepos\XComeRepo"

# Переходим в репо
Set-Location $repoDir

# 1) Подтягиваем последние изменения
git pull origin main

# 2) Удаляем всё, кроме .git
Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force

# 3) Копируем свежий билд с исключением ненужных директорий
$excludeDirs = @('Library', 'Temp', 'Obj', '.vs', '.git', '.idea', 'Logs', 'Builds', 'UserSettings')

Get-ChildItem -Path $buildDir -Recurse -Force | Where-Object {
    $fullPath = $_.FullName
    foreach ($exclude in $excludeDirs) {
        if ($fullPath -like "*\$exclude\*") {
            return $false
        }
    }
    return $true
} | ForEach-Object {
    $targetPath = $_.FullName.Replace($buildDir, $repoDir)

    if ($_.PSIsContainer) {
        if (!(Test-Path $targetPath)) {
            New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
        }
    }
    else {
        Copy-Item $_.FullName -Destination $targetPath -Force
    }
}

# 4) Делаем коммит и пушим
git add .
$ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
git commit -m "Auto-deploy WebGL build at $ts"
git push origin main
