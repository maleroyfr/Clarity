function Read-CollectorFixture($Path, $Name) {
    if ($env:CLARITY_SCRIPT_FIXTURES) {
        [void][IO.Directory]::CreateDirectory($env:CLARITY_SCRIPT_FIXTURES)
        Copy-Item -LiteralPath $Path -Destination (Join-Path $env:CLARITY_SCRIPT_FIXTURES "$Name.zip") -Force
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $data = @{}
        foreach ($entry in $zip.Entries) {
            $reader = [IO.StreamReader]::new($entry.Open())
            try {
                $text = $reader.ReadToEnd()
                $data[$entry.Name] = if ($entry.Name.EndsWith('.jsonl')) {
                    $text -split "`n" | Where-Object { $_.Trim() } | ConvertFrom-Json
                } else { $text | ConvertFrom-Json }
            } finally { $reader.Dispose() }
        }
        return $data
    } finally { $zip.Dispose() }
}
