# Read-only probe: exactly one public word, no retries, no game text.
# Invoke with the same PowerShell executable used for the earlier successful probe.
# Do not add -UserAgent: the successful probe used Invoke-WebRequest's default.
$ErrorActionPreference = 'Stop'
$probe = [ordered]@{
    ProbeTimeUTC = [DateTimeOffset]::UtcNow.ToString('o')
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    PowerShellEdition = $PSVersionTable.PSEdition
    SourceText = 'Play'
    ExplicitUserAgent = $false
    PageStatus = $null
    PageCacheControl = $null
    PageAge = $null
    PageDate = $null
    NormalTranslatorInput = $false
    IGMatchCount = 0
    IIDMatchCount = 0
    KeyTokenMatched = $false
    TokenContainsBackslash = $false
    SessionCookieCount = 0
    SessionUserAgent = $null
    TranslationRequestSent = $false
    TranslationStatus = $null
    RetryAfter = $null
    Translation = $null
    Result = $null
}

try {
    $page = Invoke-WebRequest -Uri 'https://www.bing.com/translator' `
        -UseBasicParsing -SessionVariable bingProbeSession -TimeoutSec 20
    $probe.PageStatus = [int]$page.StatusCode
    $probe.PageCacheControl = [string]$page.Headers['Cache-Control']
    $probe.PageAge = [string]$page.Headers['Age']
    $probe.PageDate = [string]$page.Headers['Date']
    $probe.NormalTranslatorInput = $page.Content.Contains('tta_input_ta')
    $probe.SessionCookieCount = $bingProbeSession.Cookies.Count
    $probe.SessionUserAgent = $bingProbeSession.UserAgent

    # These are deliberately the same first-match patterns as the successful probe.
    $igMatches = [regex]::Matches($page.Content, 'IG:"([^"]+)"')
    $iidMatches = [regex]::Matches($page.Content, 'data-iid="([^"]+)"')
    $abuseMatch = [regex]::Match($page.Content,
        'params_AbusePreventionHelper\s*=\s*\[\s*([0-9]+)\s*,\s*"([^"]+)"\s*,\s*([0-9]+)\s*\]')
    $probe.IGMatchCount = $igMatches.Count
    $probe.IIDMatchCount = $iidMatches.Count
    $probe.KeyTokenMatched = $abuseMatch.Success

    if (-not $probe.NormalTranslatorInput -or $igMatches.Count -eq 0 `
        -or $iidMatches.Count -eq 0 -or -not $abuseMatch.Success) {
        $probe.Result = 'Stopped: normal translator page parameters unavailable.'
    }
    else {
        # No HTML/JSON/URL decoding of the regex captures was used in the success case.
        $probe.TokenContainsBackslash = $abuseMatch.Groups[2].Value.Contains('\')
        $body = @{
            fromLang = 'en'
            text = 'Play'
            to = 'zh-Hans'
            key = $abuseMatch.Groups[1].Value
            token = $abuseMatch.Groups[2].Value
        }
        $probeUrl = 'https://www.bing.com/ttranslatev3?isVertical=1&IG=' `
            + [uri]::EscapeDataString($igMatches[0].Groups[1].Value) `
            + '&IID=' + [uri]::EscapeDataString($iidMatches[0].Groups[1].Value) + '.1'
        $probe.TranslationRequestSent = $true
        $reply = Invoke-WebRequest -Uri $probeUrl -Method Post -Body $body `
            -ContentType 'application/x-www-form-urlencoded' `
            -Headers @{
                Origin = 'https://www.bing.com'
                Referer = 'https://www.bing.com/translator'
                Accept = '*/*'
            } -WebSession $bingProbeSession -UseBasicParsing -TimeoutSec 20
        $probe.TranslationStatus = [int]$reply.StatusCode
        $responseData = $reply.Content | ConvertFrom-Json
        $translated = $responseData[0].translations[0]
        if ($translated.to -eq 'zh-Hans' -and -not [string]::IsNullOrWhiteSpace($translated.text)) {
            $probe.Translation = [string]$translated.text
            $probe.Result = 'Succeeded: one public word only; no retry.'
        }
        else {
            $probe.Result = 'Stopped: translation JSON did not contain the expected result.'
        }
    }
}
catch {
    # Print no raw request, HTML, response body, credential, token, or cookie.
    if ($_.Exception.Response) {
        $failedStatus = [int]$_.Exception.Response.StatusCode
        if ($probe.TranslationRequestSent) {
            $probe.TranslationStatus = $failedStatus
        }
        else {
            $probe.PageStatus = $failedStatus
        }
        $probe.RetryAfter = [string]$_.Exception.Response.Headers['Retry-After']
        $probe.Result = 'Stopped after HTTP failure; no retry or challenge handling.'
    }
    else {
        $probe.Result = 'Stopped after network or parsing failure; no retry.'
    }
}

[pscustomobject]$probe | ConvertTo-Json -Depth 3
