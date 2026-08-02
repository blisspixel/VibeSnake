function Get-AbsoluteEnvironmentPathOrDefault {
    param(
        [Parameter()][string]$ConfiguredPath,
        [Parameter(Mandatory)][string]$DefaultPath
    )

    if ($ConfiguredPath -and [System.IO.Path]::IsPathRooted($ConfiguredPath)) {
        return [System.IO.Path]::GetFullPath($ConfiguredPath)
    }
    if (-not [System.IO.Path]::IsPathRooted($DefaultPath)) {
        throw "The platform data-path fallback must be absolute."
    }
    return [System.IO.Path]::GetFullPath($DefaultPath)
}
