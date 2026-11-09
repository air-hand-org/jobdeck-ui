using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal record PackageResult(object Manifest, string Id, string Version, bool HasErrors);

internal static class PackageReader
{
    public static async Task<PackageResult> Read(Archive archive, string output, SourceResolver resolver)
    {
        using var zip = ZipFile.OpenRead(archive.Path);
        var nuspec = zip.Entries.Single(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        var xml = XDocument.Load(stream);
        var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
        XElement? Field(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name);
        if (!string.Equals(Field("id")?.Value, archive.Value.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Field("version")?.Value, archive.Value.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Nuspec identity mismatch");
        }

        var license = Field("license");
        var licenseFile = license?.Attribute("type")?.Value == "file" ? license.Value : null;
        if (licenseFile != null)
        {
            SafePath(licenseFile);
        }

        var documents = new List<object>();
        foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var path = entry.FullName;
            if (path.EndsWith('/'))
            {
                continue;
            }

            var name = Path.GetFileName(path);
            var isDocument = path == licenseFile || Regex.IsMatch(name,
                @"\A(?:licen[cs]e|notice|third[-_]party[-_]notices|copying|copyright)(?:[._-].*)?\z", RegexOptions.IgnoreCase);
            // Retain bundled attribution trees as shipped, including README/provenance.
            if (!isDocument && !path.Split('/').Any(p => p.Equals("third_party", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            using var input = entry.Open();
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            var content = bytes.ToArray();
            var destination = Path.Combine(output, "packages", archive.Key, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, content);
            documents.Add(new { path, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), bytes = content.Length });
        }
        var errors = new List<object>();
        var sourceDocuments = new List<object>();
        var sourceAttempts = new List<object>();
        var hasLicense = zip.Entries.Any(e => !e.FullName.EndsWith('/') &&
            (e.FullName == licenseFile || IsLicense(Path.GetFileName(e.FullName))));
        SourceResolver.Result? resolution = null;
        var sourceRetrieval = "not-needed-package-license-present";
        if (!hasLicense)
        {
            sourceRetrieval = "nuspec-commit-then-exact-version-tags";
            var repository = Field("repository");
            var url = repository?.Attribute("url")?.Value;
            var match = Regex.Match(url ?? "", @"\Ahttps://github\.com/([A-Za-z0-9_-][A-Za-z0-9_.-]*)/([A-Za-z0-9_-][A-Za-z0-9_.-]*?)(?:\.git)?/?\z");
            if (!match.Success)
            {
                errors.Add(new { code = string.IsNullOrEmpty(url) ? "missing-repository" : "unsafe-or-unsupported-repository" });
            }
            else
            {
                resolution = await resolver.Resolve($"{match.Groups[1].Value}/{match.Groups[2].Value}", archive.Value.Version, repository?.Attribute("commit")?.Value);
                sourceAttempts.AddRange(resolution.Attempts);
                errors.AddRange(resolution.Errors);
                if (resolution.Error != null)
                {
                    errors.Add(new { code = resolution.Error });
                }

                foreach (var document in resolution.Documents)
                {
                    var path = document.Revision + "/" + document.Name;
                    var destination = Path.Combine(output, "source", archive.Key, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, document.Bytes);
                    sourceDocuments.Add(new
                    {
                        path,
                        url = document.Url,
                        revision = document.Revision,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(document.Bytes)),
                        bytes = document.Bytes.Length
                    });
                    if (IsLicense(document.Name))
                    {
                        hasLicense = true;
                    }
                }
            }
        }
        if (!hasLicense)
        {
            errors.Add(new { code = "unresolved-original-license" });
        }

        var declaredLicenseFileMissing = licenseFile != null && !zip.Entries.Any(e => e.FullName == licenseFile);
        if (declaredLicenseFileMissing)
        {
            errors.Add(new { code = "declared-license-file-missing" });
        }

        var manifest = new
        {
            id = archive.Value.Id,
            version = archive.Value.Version,
            source = "restored-nupkg",
            packagePath = archive.Key + "/" + Path.GetFileName(archive.Path),
            nupkgSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive.Path))),
            nuspecPath = nuspec.FullName,
            licenseType = license?.Attribute("type")?.Value,
            license = license?.Value,
            licenseUrl = Field("licenseUrl")?.Value,
            repository = Field("repository")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
            status = errors.Count > 0 ? "error" : "original-license-collected",
            declaredLicenseFileMissing,
            sourceRetrieval,
            sourceResolution = resolution == null ? null : new
            {
                nuspecCommit = Field("repository")?.Attribute("commit")?.Value,
                tag = resolution.Tag,
                resolvedSha = resolution.ResolvedSha,
                provenance = resolution.Provenance,
                fallbackReason = resolution.FallbackReason,
                nuspecCommitMismatch = resolution.ResolvedSha == null || string.IsNullOrEmpty(Field("repository")?.Attribute("commit")?.Value)
                    ? (bool?)null : !string.Equals(resolution.ResolvedSha, Field("repository")?.Attribute("commit")?.Value, StringComparison.OrdinalIgnoreCase),
                tagCandidates = resolution.Tags.Select(t => new { tag = t.Tag, resolvedSha = t.Sha, error = t.Error })
            },
            documents,
            sourceDocuments,
            sourceAttempts,
            errors,
            attributionScope = "Bundled documents first; fallback checks standard repository-root names only. A collected LICENSE does not establish complete attribution. Packaged licenses may omit source NOTICE files; those are not probed when a package license is present."
        };
        return new(manifest, archive.Value.Id, archive.Value.Version, errors.Count > 0);
    }

    internal static bool IsLicense(string name) => Regex.IsMatch(name, @"\A(?:licen[cs]e|copying)(?:[._-].*)?\z", RegexOptions.IgnoreCase);

    internal static void SafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p is ".." or "."))
        {
            throw new InvalidDataException("Unsafe archive path");
        }
    }
}
