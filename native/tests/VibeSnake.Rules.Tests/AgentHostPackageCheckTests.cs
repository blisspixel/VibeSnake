using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class AgentHostPackageCheckTests
{
    private static readonly string[] HostLocks =
    [
        "native/src/VibeSnake.AgentPlay/packages.lock.json",
        "native/src/VibeSnake.Persistence/packages.lock.json",
        "native/src/VibeSnake.Rules/packages.lock.json",
        "native/tools/VibeSnake.AgentHost/packages.lock.json",
    ];

    [Fact]
    public void Closed_unsigned_linux_package_passes()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package);
            var result = AgentHostPackageCheck.Inspect(package, repository);
            Assert.True(result.Passed, string.Join('\n', result.Failures));
            Assert.Contains(Path.GetFullPath(package), result.SuccessMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Windows_osx_nested_runtime_and_nuget_hash_packages_pass()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(
                package,
                editManifest: manifest =>
                {
                    manifest["runtime_identifier"] = "win-x64";
                    manifest["executable"] = "VibeSnake.AgentHost.exe";
                    manifest["host_version"] = "00.18.0";
                },
                editInventory: inventory =>
                {
                    inventory["source_dirty"] = true;
                    var hosted = inventory["packages"]!.AsArray()[0]!.AsObject();
                    hosted["dependency_types"] = new JsonArray("direct", "transitive");
                    hosted["content_hashes"] = new JsonArray(
                        "vUl798SmruTqqlt/xH2gDk3tJlhk6k3HdOXAHirlRfbNKDym4g/kRpUL9S4sl6F6FsOTOMW+ZsDapqlZMOOiEw==");
                    inventory["packages"]!.AsArray()[1]!.AsObject()["content_hashes"] = new JsonArray();
                });
            AssertPassed(package, repository);

            WritePackage(
                package,
                editManifest: manifest =>
                {
                    manifest["runtime_identifier"] = "osx-arm64";
                    manifest["executable"] = "VibeSnake.AgentHost";
                });
            var nested = Path.Combine(package, "runtimes", "osx-arm64", "native");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "coreclr.dll"), "dll");
            WriteChecksums(package);
            AssertPassed(package, repository);
        });
    }

    [Fact]
    public void Protocol_follows_the_agent_host_constant()
    {
        WithPackage((package, repository) =>
        {
            WriteProgram(repository, "1999-01-01");
            WritePackage(package, editManifest: manifest => manifest["protocol_version"] = "1999-01-01");
            AssertPassed(package, repository);

            WritePackage(package);
            AssertFails(package, repository, "protocol_version must be 1999-01-01");

            var realRoot = AgentKnowledgeTestRepository.ResolveRepositoryRoot();
            WritePackage(package);
            AssertPassed(package, realRoot);
        });
    }

    [Fact]
    public void Publication_self_contained_and_player_files_are_rejected()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package, editManifest: manifest => manifest["publication_eligible"] = true);
            AssertFails(package, repository, "publication_eligible must stay false");

            WritePackage(
                package,
                editManifest: manifest =>
                {
                    manifest["self_contained"] = false;
                    manifest["framework_dependent"] = true;
                });
            AssertFails(package, repository, "self_contained must be true", "framework_dependent must be false");

            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "preferences.json"), "{}\n");
            Directory.CreateDirectory(Path.Combine(package, "nested"));
            File.WriteAllText(Path.Combine(package, "nested", "mcp.json"), "{}\n");
            WriteChecksums(package);
            AssertFails(package, repository, "player or plugin files");
        });
    }

    [Fact]
    public void Checksums_executables_fields_and_inventory_identity_are_closed()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package);
            File.WriteAllBytes(Path.Combine(package, "extra.bin"), "extra"u8.ToArray());
            AssertFails(package, repository, "every packaged regular file");

            WritePackage(
                package,
                editManifest: manifest =>
                {
                    manifest["runtime_identifier"] = "win-x64";
                    manifest["executable"] = "VibeSnake.AgentHost";
                });
            AssertFails(package, repository, "Windows packages must declare a .exe host");

            WritePackage(
                package,
                editManifest: manifest => manifest["executable"] = "VibeSnake.AgentHost.exe");
            AssertFails(package, repository, "non-Windows packages must not declare a .exe host");

            WritePackage(package, editManifest: manifest => manifest["grade"] = "A+");
            AssertFails(package, repository, "unknown fields: grade");

            WritePackage(package);
            File.Delete(Path.Combine(package, "host-inventory.json"));
            WriteChecksums(package);
            AssertFails(package, repository, "host-inventory.json: required packaged regular file is missing");

            WritePackage(
                package,
                editInventory: inventory =>
                {
                    inventory["packages"]!.AsArray().Add(PackageNode("python", "ruff", "0.0.0"));
                });
            AssertFails(package, repository, "ecosystem must be nuget");

            WritePackage(package, editProvenance: provenance => provenance["executable_sha256"] = new string('f', 64));
            AssertFails(package, repository, "executable_sha256 must match the declared host");

            WritePackage(package, editProvenance: provenance => provenance["publication_eligible"] = true);
            AssertFails(package, repository, "host-provenance.json: publication_eligible must stay false");

            WritePackage(
                package,
                editInventory: inventory => inventory["packages"]!.AsArray().RemoveAt(1));
            AssertFails(package, repository, "missing required package ModelContextProtocol");

            WritePackage(
                package,
                editInventory: inventory => inventory["packages"]!.AsArray().RemoveAt(0));
            AssertFails(package, repository, "missing required package Microsoft.Extensions.Hosting");
        });
    }

    [Fact]
    public void Malformed_json_bounds_and_manifest_rules_fail_closed()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "host-manifest.json"), "[]\n");
            WriteChecksums(package);
            AssertFails(package, repository, "host-manifest.json: root must be an object");

            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "host-inventory.json"), "{\"schema\":\"a\",\"schema\":\"b\"}\n");
            WriteChecksums(package);
            AssertFails(package, repository, "duplicate JSON key: schema");

            WritePackage(package);
            File.WriteAllBytes(Path.Combine(package, "host-provenance.json"), [0xFF]);
            WriteChecksums(package);
            AssertFails(package, repository, "host-provenance.json: unreadable JSON");

            WritePackage(package);
            File.WriteAllBytes(Path.Combine(package, "host-manifest.json"), new byte[(128 * 1024) + 1]);
            WriteChecksums(package);
            AssertFails(package, repository, "host-manifest.json: JSON exceeds the 131072-byte validation limit");

            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "host-inventory.json"), "{\"schema\":\"x\",}\n");
            WriteChecksums(package);
            AssertFails(package, repository, "host-inventory.json: unreadable JSON");

            WritePackage(package, editManifest: manifest => manifest.Remove("schema"));
            AssertFails(
                package,
                repository,
                "missing fields: schema",
                "schema must be vibesnake-agent-host-package-v1");

            WritePackage(package, editManifest: manifest => manifest["host_version"] = "0.18.0-alpha.1");
            AssertFails(package, repository, "host_version must be dotted numeric SemVer");

            WritePackage(package, editManifest: manifest => manifest["runtime_identifier"] = "linux-musl-x64");
            AssertFails(package, repository, "runtime_identifier must be a closed desktop RID");

            WritePackage(package, editManifest: manifest => manifest["signing"] = "signed");
            AssertFails(package, repository, "signing must be unsigned");

            WritePackage(package, editManifest: manifest => manifest["transport"] = "http");
            AssertFails(package, repository, "transport must be stdio");

            WritePackage(package, editManifest: manifest => manifest["user_data_policy"] = "elsewhere");
            AssertFails(package, repository, "user_data_policy must be godot-app-userdata");

            WritePackage(package, editManifest: manifest => manifest["host_name"] = "other-host");
            AssertFails(package, repository, "host_name must be vibesnake-agent-host");

            WritePackage(package, editManifest: manifest => manifest["schema"] = "other-schema");
            AssertFails(package, repository, "schema must be vibesnake-agent-host-package-v1");

            WritePackage(package, editManifest: manifest => manifest["executable"] = "nested/host");
            AssertFails(package, repository, "executable must be a file name in the package root");

            WritePackage(
                package,
                editManifest: manifest =>
                {
                    manifest["runtime_identifier"] = "win-x64";
                    manifest["executable"] = "MissingHost.exe";
                });
            File.Delete(Path.Combine(package, "MissingHost.exe"));
            WriteChecksums(package);
            var missing = Inspect(package, repository);
            Assert.Contains("MissingHost.exe: declared host executable is missing", Text(missing), StringComparison.Ordinal);
            Assert.DoesNotContain("Windows packages must declare a .exe host", Text(missing), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Inventory_provenance_and_checksum_rules_fail_closed()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package, editInventory: inventory => inventory["generated_from_locks_only"] = false);
            AssertFails(package, repository, "generated_from_locks_only must be true");

            WritePackage(package, editInventory: inventory => inventory["source_revision"] = "abc");
            AssertFails(package, repository, "source_revision must be a 40-character lowercase SHA-1");

            WritePackage(package, editInventory: inventory => inventory["source_dirty"] = 1);
            AssertFails(package, repository, "source_dirty must be a boolean");

            WritePackage(package, editInventory: inventory => inventory["lock_set_sha256"] = new string('A', 64));
            AssertFails(package, repository, "lock_set_sha256 must be a lowercase SHA-256");

            WritePackage(package, editInventory: inventory => inventory["dotnet_sdk"] = "10.0");
            AssertFails(package, repository, "dotnet_sdk must be dotted numeric SemVer");

            WritePackage(package, editInventory: inventory => inventory["lock_set_sha256"] = new string('f', 64));
            AssertFails(package, repository, "lock_set_sha256 must match the ordered source list");

            WritePackage(
                package,
                editInventory: inventory =>
                {
                    var sources = inventory["sources"]!.AsArray();
                    var swapped = new JsonArray
                    {
                        sources[1]!.DeepClone(),
                        sources[0]!.DeepClone(),
                    };
                    for (var index = 2; index < sources.Count; index++)
                    {
                        swapped.Add(sources[index]!.DeepClone());
                    }

                    inventory["sources"] = swapped;
                });
            AssertFails(package, repository, "sources must be the exact host lock closure in path order");

            WritePackage(
                package,
                editInventory: inventory =>
                {
                    inventory["packages"] = new JsonArray(
                        PackageNode("nuget", "ModelContextProtocol", "2.2.0"),
                        PackageNode("nuget", "Microsoft.Extensions.Hosting", "10.0.11"));
                });
            AssertFails(package, repository, "packages must be sorted by name then version");

            WritePackage(
                package,
                editInventory: inventory =>
                    inventory["packages"]!.AsArray().Add(PackageNode("nuget", "modelcontextprotocol", "2.2.0")));
            AssertFails(package, repository, "duplicate package modelcontextprotocol 2.2.0");

            WritePackage(
                package,
                editManifest: manifest =>
                {
                    manifest["runtime_identifier"] = "win-x64";
                    manifest["executable"] = "VibeSnake.AgentHost.exe";
                },
                editInventory: inventory =>
                    inventory["packages"]!.AsArray()[0]!.AsObject()["frameworks"] = new JsonArray("net10.0/linux-x64"));
            AssertFails(package, repository, "framework net10.0/linux-x64 is outside this package RID");

            WritePackage(
                package,
                editInventory: inventory =>
                    inventory["packages"]!.AsArray()[0]!.AsObject()["source_locks"] =
                        new JsonArray("native/not-a-lock.json"));
            AssertFails(package, repository, "source lock is outside the host lock closure");

            WritePackage(package, editProvenance: provenance => provenance["host_version"] = "9.9.9");
            AssertFails(package, repository, "host_version must match host-manifest.json");

            WritePackage(package, editProvenance: provenance => provenance["manifest_sha256"] = new string('e', 64));
            AssertFails(package, repository, "manifest_sha256 must match host-manifest.json");

            WritePackage(package, editProvenance: provenance => provenance["inventory_sha256"] = new string('d', 64));
            AssertFails(package, repository, "inventory_sha256 must match host-inventory.json");

            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "SHA256SUMS"), "nope\n");
            AssertFails(package, repository, "SHA256SUMS:1: invalid checksum entry");

            WritePackage(package);
            var license = Path.Combine(package, "LICENSE");
            File.WriteAllText(license, "changed\n");
            AssertFails(package, repository, "SHA256SUMS: digest mismatch for LICENSE");

            WritePackage(package);
            File.Delete(Path.Combine(package, "SHA256SUMS"));
            var missingSums = Text(Inspect(package, repository));
            Assert.Contains("packaged host requires a complete checksum manifest", missingSums, StringComparison.Ordinal);
            Assert.DoesNotContain("exactly once", missingSums, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Symbols_case_names_tree_limit_and_roots_are_closed()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "Foo.PDB"), "symbols");
            WriteChecksums(package);
            AssertFails(package, repository, "Foo.PDB: debug symbols are not part of this package");

            WritePackage(package);
            File.WriteAllText(Path.Combine(package, "Preferences.json"), "{}\n");
            WriteChecksums(package);
            AssertPassed(package, repository);

            WritePackage(package);
            for (var index = 0; index < 8193; index++)
            {
                File.WriteAllText(Path.Combine(package, index.ToString("x4", CultureInfo.InvariantCulture)), "x");
            }

            AssertFails(package, repository, "host package tree exceeds the 8192-entry validation limit");

            var missing = Path.Combine(package, "missing-root");
            var missingResult = AgentHostPackageCheck.Inspect(missing, repository);
            Assert.Equal(["host package root must be an existing directory"], missingResult.Failures);

            var fileRoot = Path.Combine(package, "not-a-directory");
            File.WriteAllText(fileRoot, "x");
            var fileResult = AgentHostPackageCheck.Inspect(fileRoot, repository);
            Assert.Equal(["host package root must be an existing directory"], fileResult.Failures);

            Assert.Throws<ArgumentException>(() => AgentHostPackageCheck.Inspect(" ", repository));
            Assert.Throws<ArgumentException>(() => AgentHostPackageCheck.Inspect(package, " "));
        });
    }

    [Fact]
    public void Command_reports_success_failure_and_usage_on_separate_streams()
    {
        WithPackage((package, repository) =>
        {
            WritePackage(package);
            var output = new StringWriter();
            var error = new StringWriter();
            var code = RepositoryCheckCommand.Run(["host-package", package, repository], output, error);
            Assert.Equal(0, code);
            Assert.Contains("Agent Host package validation passed:", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            WritePackage(package, editManifest: manifest => manifest["publication_eligible"] = true);
            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(["host-package", package, repository], output, error);
            Assert.Equal(1, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Agent Host package check failed:", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(["host-package"], output, error);
            Assert.Equal(2, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            code = RepositoryCheckCommand.Run(["host-package", "bad\0root"], output, error);
            Assert.Equal(2, code);
            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Agent Host package root is invalid.", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static void AssertPassed(string package, string repository)
    {
        var result = AgentHostPackageCheck.Inspect(package, repository);
        Assert.True(result.Passed, string.Join('\n', result.Failures));
    }

    private static void AssertFails(string package, string repository, params string[] fragments)
    {
        var text = Text(Inspect(package, repository));
        Assert.False(Inspect(package, repository).Passed);
        foreach (var fragment in fragments)
        {
            Assert.Contains(fragment, text, StringComparison.Ordinal);
        }
    }

    private static RepositoryCheckResult Inspect(string package, string repository) =>
        AgentHostPackageCheck.Inspect(package, repository);

    private static string Text(RepositoryCheckResult result) => string.Join('\n', result.Failures);

    private static void WithPackage(Action<string, string> body)
    {
        var package = Directory.CreateTempSubdirectory("vibesnake-host-package-").FullName;
        var repository = Directory.CreateTempSubdirectory("vibesnake-host-repo-").FullName;
        try
        {
            WriteProgram(repository, "2026-07-28");
            body(package, repository);
        }
        finally
        {
            Directory.Delete(package, recursive: true);
            Directory.Delete(repository, recursive: true);
        }
    }

    private static void WriteProgram(string repository, string protocol)
    {
        var directory = Path.Combine(repository, "native", "tools", "VibeSnake.AgentHost");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "Program.cs"),
            $"public const string McpProtocolVersion = \"{protocol}\";\n",
            new UTF8Encoding(false));
    }

    private static void WritePackage(
        string root,
        Action<JsonObject>? editManifest = null,
        Action<JsonObject>? editInventory = null,
        Action<JsonObject>? editProvenance = null)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        var manifest = new JsonObject
        {
            ["schema"] = "vibesnake-agent-host-package-v1",
            ["host_name"] = "vibesnake-agent-host",
            ["host_version"] = "0.17.0",
            ["runtime_identifier"] = "linux-x64",
            ["self_contained"] = true,
            ["framework_dependent"] = false,
            ["publication_eligible"] = false,
            ["executable"] = "VibeSnake.AgentHost",
            ["protocol_version"] = "2026-07-28",
            ["transport"] = "stdio",
            ["user_data_policy"] = "godot-app-userdata",
            ["signing"] = "unsigned",
        };
        editManifest?.Invoke(manifest);
        var executable = manifest["executable"]!.GetValue<string>();
        WriteExecutable(root, executable);
        File.WriteAllText(Path.Combine(root, "LICENSE"), "license\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "NOTICE"), "notice\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "INSTALL.txt"), "install\n", new UTF8Encoding(false));
        WriteJson(Path.Combine(root, "host-manifest.json"), manifest);

        var sources = new JsonArray();
        foreach (var path in HostLocks)
        {
            sources.Add(new JsonObject
            {
                ["path"] = path,
                ["sha256"] = new string('a', 64),
            });
        }

        var inventory = new JsonObject
        {
            ["schema"] = "vibesnake-agent-host-inventory-v1",
            ["generated_from_locks_only"] = true,
            ["host_version"] = manifest["host_version"]!.GetValue<string>(),
            ["runtime_identifier"] = manifest["runtime_identifier"]!.GetValue<string>(),
            ["source_revision"] = new string('b', 40),
            ["source_dirty"] = false,
            ["lock_set_sha256"] = LockSetHash(sources),
            ["dotnet_sdk"] = "10.0.303",
            ["sources"] = sources,
            ["packages"] = new JsonArray(
                PackageNode("nuget", "Microsoft.Extensions.Hosting", "10.0.11"),
                PackageNode("nuget", "ModelContextProtocol", "2.2.0")),
        };
        editInventory?.Invoke(inventory);
        WriteJson(Path.Combine(root, "host-inventory.json"), inventory);

        var executablePath = ExistingExecutable(root, executable);
        var provenance = new JsonObject
        {
            ["schema"] = "vibesnake-agent-host-provenance-v1",
            ["host_name"] = "vibesnake-agent-host",
            ["host_version"] = inventory["host_version"]!.GetValue<string>(),
            ["runtime_identifier"] = inventory["runtime_identifier"]!.GetValue<string>(),
            ["source_revision"] = inventory["source_revision"]!.GetValue<string>(),
            ["source_dirty"] = inventory["source_dirty"]!.DeepClone(),
            ["self_contained"] = true,
            ["signing"] = "unsigned",
            ["publication_eligible"] = false,
            ["executable_sha256"] = executablePath is null ? new string('0', 64) : Hash(executablePath),
            ["manifest_sha256"] = Hash(Path.Combine(root, "host-manifest.json")),
            ["inventory_sha256"] = Hash(Path.Combine(root, "host-inventory.json")),
            ["lock_set_sha256"] = inventory["lock_set_sha256"]!.GetValue<string>(),
            ["dotnet_sdk"] = inventory["dotnet_sdk"]!.GetValue<string>(),
        };
        editProvenance?.Invoke(provenance);
        WriteJson(Path.Combine(root, "host-provenance.json"), provenance);
        WriteChecksums(root);
    }

    private static void WriteExecutable(string root, string executable)
    {
        if (executable.Contains('\\') || executable.Contains('/'))
        {
            return;
        }

        File.WriteAllBytes(Path.Combine(root, executable), "host"u8.ToArray());
    }

    private static string? ExistingExecutable(string root, string executable)
    {
        if (executable.Contains('\\') || executable.Contains('/'))
        {
            return null;
        }

        var path = Path.Combine(root, executable);
        return File.Exists(path) ? path : null;
    }

    private static JsonObject PackageNode(string ecosystem, string name, string version) => new()
    {
        ["ecosystem"] = ecosystem,
        ["name"] = name,
        ["version"] = version,
        ["dependency_types"] = new JsonArray("direct"),
        ["source_locks"] = new JsonArray(HostLocks[^1]),
        ["content_hashes"] = new JsonArray(new string('c', 64)),
        ["frameworks"] = new JsonArray("net10.0"),
    };

    private static string LockSetHash(JsonArray sources)
    {
        var lines = sources.Select(source =>
        {
            var item = source!.AsObject();
            return item["path"]!.GetValue<string>() + "=" + item["sha256"]!.GetValue<string>();
        });
        return Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false).GetBytes(string.Join('\n', lines))));
    }

    private static void WriteChecksums(string root)
    {
        var lines = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Where(path => !string.Equals(Path.GetFileName(path), "SHA256SUMS", StringComparison.Ordinal))
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                return Hash(path) + "  " + relative;
            });
        File.WriteAllText(
            Path.Combine(root, "SHA256SUMS"),
            string.Join('\n', lines) + "\n",
            new UTF8Encoding(false));
    }

    private static void WriteJson(string path, JsonObject value)
    {
        File.WriteAllText(
            path,
            value.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
