# Pubblica le pagine di wiki\ nella wiki di GitHub (https://github.com/corbolma/CorbolPrompt/wiki).
#   powershell -ExecutionPolicy Bypass -File publish-wiki.ps1
# La wiki è un repository git a parte (CorbolPrompt.wiki.git): lo si clona in una cartella temporanea,
# lo si rende uguale a wiki\ (pagine nuove, cambiate e tolte) e si fa il push. Se non cambia niente non fa nulla.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'wiki'
$url = 'https://github.com/corbolma/CorbolPrompt.wiki.git'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('CorbolPrompt-wiki-' + [Guid]::NewGuid().ToString('N'))

git clone --quiet $url $tmp
if ($LASTEXITCODE -ne 0) { throw "Clonazione della wiki fallita. La wiki va creata una volta dal sito: Wiki -> Create the first page." }
try {
    # stesso autore dei commit del progetto (indirizzo anonimo di GitHub)
    git -C $tmp config user.name (git -C $root config user.name)
    git -C $tmp config user.email (git -C $root config user.email)

    Get-ChildItem $tmp -File | Remove-Item -Force          # le pagine tolte da wiki\ spariscono anche dalla wiki
    Copy-Item (Join-Path $src '*') $tmp -Recurse -Force

    git -C $tmp add -A
    if (-not (git -C $tmp status --porcelain)) { Write-Host 'La wiki è già aggiornata.'; return }
    $rev = git -C $root rev-parse --short HEAD
    git -C $tmp commit --quiet -m "Wiki aggiornata da wiki\ ($rev)"
    git -C $tmp push --quiet origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Push della wiki fallito.' }
    Write-Host 'Wiki pubblicata: https://github.com/corbolma/CorbolPrompt/wiki'
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
