Set-StrictMode -Version Latest

$script:ReleaseEntryTimestamp = [DateTimeOffset]::new(
    2000,
    1,
    1,
    0,
    0,
    0,
    [TimeSpan]::Zero)

$script:Crc32Type = 'CompanionDesktopPet.ReleasePackaging.Crc32' -as [type]
if ($null -eq $script:Crc32Type) {
    $script:Crc32Type = Add-Type -PassThru -TypeDefinition @'
using System;
using System.IO;

namespace CompanionDesktopPet.ReleasePackaging
{
    public static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(string path)
        {
            uint crc = UInt32.MaxValue;
            byte[] buffer = new byte[64 * 1024];
            using (FileStream stream = File.OpenRead(path))
            {
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int index = 0; index < count; index++)
                    {
                        crc = Table[(crc ^ buffer[index]) & 0xff] ^ (crc >> 8);
                    }
                }
            }

            return ~crc;
        }

        private static uint[] CreateTable()
        {
            uint[] table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                uint remainder = value;
                for (int bit = 0; bit < 8; bit++)
                {
                    remainder = (remainder & 1) == 0
                        ? remainder >> 1
                        : 0xedb88320u ^ (remainder >> 1);
                }

                table[value] = remainder;
            }

            return table;
        }
    }
}
'@
}

function ConvertFrom-ReleaseTag {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Tag
    )

    $match = [Regex]::Match(
        $Tag,
        '^v(?<Version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:-(?<Prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?)$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) {
        throw "Release tag must be a strict SemVer tag beginning with 'v': $Tag"
    }

    $prerelease = $match.Groups['Prerelease'].Value
    if (-not [string]::IsNullOrEmpty($prerelease)) {
        foreach ($identifier in $prerelease.Split('.')) {
            if ($identifier -cmatch '^[0-9]+$' -and
                $identifier.Length -gt 1 -and
                $identifier[0] -eq '0') {
                throw "Numeric prerelease identifiers must not contain leading zeroes: $Tag"
            }
        }
    }

    return $match.Groups['Version'].Value
}

function New-DeterministicReleaseZip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [string[]]$EntryNames,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$DestinationPath
    )

    $sourcePath = (Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "Release ZIP source must be a directory: $SourceDirectory"
    }

    $seenEntryNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($entryName in $EntryNames) {
        $hasSeparator = -not [string]::IsNullOrEmpty($entryName) -and
            ($entryName.Contains('/') -or $entryName.Contains('\'))
        $hasInvalidLeafCharacter = -not [string]::IsNullOrEmpty($entryName) -and
            $entryName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0
        if ([string]::IsNullOrWhiteSpace($entryName) -or
            $entryName -ceq '.' -or
            $entryName -ceq '..' -or
            [IO.Path]::IsPathRooted($entryName) -or
            $hasSeparator -or
            $hasInvalidLeafCharacter -or
            $entryName.EndsWith('.', [StringComparison]::Ordinal) -or
            $entryName.EndsWith(' ', [StringComparison]::Ordinal)) {
            throw "Release ZIP entry name must be a safe leaf name: $entryName"
        }

        if (-not $seenEntryNames.Add($entryName)) {
            throw "Release ZIP entry names must be unique: $entryName"
        }
    }

    $orderedEntryNames = [string[]]@($EntryNames)
    [Array]::Sort($orderedEntryNames, [StringComparer]::Ordinal)

    $destinationFullPath = [IO.Path]::GetFullPath($DestinationPath)
    $destinationDirectory = Split-Path -Parent $destinationFullPath
    if (-not [string]::IsNullOrEmpty($destinationDirectory)) {
        $null = New-Item -ItemType Directory -Path $destinationDirectory -Force
    }

    $destinationCreated = $false
    try {
        $destinationStream = [IO.File]::Open(
            $destinationFullPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        $destinationCreated = $true
        try {
        $writer = [IO.BinaryWriter]::new(
            $destinationStream,
            [Text.UTF8Encoding]::new($false),
            $true)
        try {
            if ($orderedEntryNames.Count -gt [uint16]::MaxValue) {
                throw 'Release ZIP contains too many entries for deterministic ZIP32 output.'
            }

            $utf8 = [Text.UTF8Encoding]::new($false, $true)
            $generalPurposeFlags = [uint16]0x0800
            $compressionMethod = [uint16]0
            $timestamp = $script:ReleaseEntryTimestamp.DateTime
            $dosTime = [uint16](
                ($timestamp.Hour -shl 11) -bor
                ($timestamp.Minute -shl 5) -bor
                [Math]::Floor($timestamp.Second / 2))
            $dosDate = [uint16](
                (($timestamp.Year - 1980) -shl 9) -bor
                ($timestamp.Month -shl 5) -bor
                $timestamp.Day)
            $records = [Collections.Generic.List[object]]::new()

            foreach ($entryName in $orderedEntryNames) {
                $sourceFile = Join-Path $sourcePath $entryName
                if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
                    throw "Release ZIP entry does not name a source file: $entryName"
                }

                $nameBytes = $utf8.GetBytes($entryName)
                if ($nameBytes.Length -gt [uint16]::MaxValue) {
                    throw "Release ZIP entry name is too long: $entryName"
                }

                $sourceLength = (Get-Item -LiteralPath $sourceFile).Length
                if ($sourceLength -gt [uint32]::MaxValue) {
                    throw "Release ZIP entry is too large for deterministic ZIP32 output: $entryName"
                }

                if ($destinationStream.Position -gt [uint32]::MaxValue) {
                    throw 'Release ZIP exceeds the deterministic ZIP32 offset limit.'
                }

                $localHeaderOffset = [uint32]$destinationStream.Position
                $crc32 = [uint32]$script:Crc32Type::Compute([string]$sourceFile)
                $length = [uint32]$sourceLength

                $writer.Write([uint32]0x04034b50)
                $writer.Write([uint16]20)
                $writer.Write($generalPurposeFlags)
                $writer.Write($compressionMethod)
                $writer.Write($dosTime)
                $writer.Write($dosDate)
                $writer.Write($crc32)
                $writer.Write($length)
                $writer.Write($length)
                $writer.Write([uint16]$nameBytes.Length)
                $writer.Write([uint16]0)
                $writer.Write($nameBytes)
                $writer.Flush()

                $inputStream = [IO.File]::OpenRead($sourceFile)
                try {
                    $inputStream.CopyTo($destinationStream)
                }
                finally {
                    $inputStream.Dispose()
                }

                $records.Add([pscustomobject]@{
                    NameBytes = $nameBytes
                    Crc32 = $crc32
                    Length = $length
                    LocalHeaderOffset = $localHeaderOffset
                })
            }

            if ($destinationStream.Position -gt [uint32]::MaxValue) {
                throw 'Release ZIP exceeds the deterministic ZIP32 offset limit.'
            }

            $centralDirectoryOffset = [uint32]$destinationStream.Position
            foreach ($record in $records) {
                $writer.Write([uint32]0x02014b50)
                $writer.Write([uint16]20)
                $writer.Write([uint16]20)
                $writer.Write($generalPurposeFlags)
                $writer.Write($compressionMethod)
                $writer.Write($dosTime)
                $writer.Write($dosDate)
                $writer.Write([uint32]$record.Crc32)
                $writer.Write([uint32]$record.Length)
                $writer.Write([uint32]$record.Length)
                $writer.Write([uint16]$record.NameBytes.Length)
                $writer.Write([uint16]0)
                $writer.Write([uint16]0)
                $writer.Write([uint16]0)
                $writer.Write([uint16]0)
                $writer.Write([uint32]0)
                $writer.Write([uint32]$record.LocalHeaderOffset)
                $writer.Write([byte[]]$record.NameBytes)
            }

            $writer.Flush()
            $centralDirectorySize = $destinationStream.Position - $centralDirectoryOffset
            if ($centralDirectorySize -gt [uint32]::MaxValue) {
                throw 'Release ZIP central directory exceeds the deterministic ZIP32 size limit.'
            }

            $entryCount = [uint16]$records.Count
            $writer.Write([uint32]0x06054b50)
            $writer.Write([uint16]0)
            $writer.Write([uint16]0)
            $writer.Write($entryCount)
            $writer.Write($entryCount)
            $writer.Write([uint32]$centralDirectorySize)
            $writer.Write($centralDirectoryOffset)
            $writer.Write([uint16]0)
            $writer.Flush()
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $destinationStream.Dispose()
    }
    }
    catch {
        if ($destinationCreated) {
            [IO.File]::Delete($destinationFullPath)
        }

        throw
    }
}

Export-ModuleMember -Function @(
    'ConvertFrom-ReleaseTag'
    'New-DeterministicReleaseZip'
)
