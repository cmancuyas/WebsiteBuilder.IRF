param(
  [string]$Base = "http://localhost:5000",
  [string]$Slug = "/"
)

function Show-Headers($url) {
  Write-Host "GET $url"
  curl.exe -s -I $url | Select-String -Pattern "HTTP/|x-outputcache|cache-control|set-cookie|location" -CaseSensitive:$false
  Write-Host ""
}

Write-Host "1) First request (expect MISS)"
Show-Headers "$Base$Slug"

Write-Host "2) Second request (expect HIT)"
Show-Headers "$Base$Slug"

Write-Host "3) Preview request (expect no-store and never HIT)"
Show-Headers "$Base$Slug?preview=1"

Write-Host "4) Redirect request (expect 301/302 and never cached)"
Show-Headers "$Base/some-old-slug"
