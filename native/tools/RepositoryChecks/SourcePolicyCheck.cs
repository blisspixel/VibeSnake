using System.Text;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public sealed record SourcePolicyViolation(string Path, int Line, string Message)
{
    public string Render() => $"{Path}:{Line}: {Message}";
}

public static partial class SourcePolicyCheck
{
    private static readonly string[] ScanRoots =
    [
        ".github",
        "docs",
        "game",
        "native",
        "scripts",
        "src",
        "tests",
    ];

    private static readonly string[] RootFiles =
    [
        ".gitattributes",
        ".editorconfig",
        ".gitignore",
        ".pre-commit-config.yaml",
        "CHANGELOG.md",
        "CODE_OF_CONDUCT.md",
        "CONTRIBUTING.md",
        "Directory.Build.props",
        "LICENSE",
        "NOTICE",
        "pyproject.toml",
        "README.md",
        "ROADMAP.md",
        "SECURITY.md",
        "SUPPORT.md",
    ];

    private static readonly string[] SupportingFiles =
    [
        "assets/README.md",
        "config/README.md",
        "data/README.md",
    ];

    private static readonly HashSet<string> TextSuffixes = new(
        [
            ".cfg",
            ".cs",
            ".csproj",
            ".gd",
            ".godot",
            ".ini",
            ".md",
            ".props",
            ".ps1",
            ".py",
            ".slnx",
            ".toml",
            ".tscn",
            ".tres",
            ".xml",
            ".yaml",
            ".yml",
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ExcludedParts = new(
        [
            ".agent",
            ".dotnet",
            ".git",
            ".godot",
            ".mypy_cache",
            ".pytest_cache",
            ".ruff_cache",
            ".tools",
            ".venv",
            "__pycache__",
            "bin",
            "obj",
            "TestResults",
            "artifacts",
            "build",
            "dist",
            "venv",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> MarkerExemptions = new(
        ["docs/engineering/CODE_QUALITY_STANDARDS.md"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ForbiddenCredentialSuffixes = new(
        [".p12", ".p8", ".pem", ".pfx", ".jks", ".keystore", ".key"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex MarkerPattern = BuildMarkerPattern();
    private static readonly Regex AttributionPattern = BuildAttributionPattern();

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            return Failed(["repository root does not exist"]);
        }

        IReadOnlyList<string> policyFiles;
        var violations = new List<SourcePolicyViolation>();
        try
        {
            policyFiles = PolicyFiles(root);
            violations.AddRange(CredentialViolations(root));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return Failed([$"could not enumerate repository files: {SingleLine(exception.Message)}"]);
        }

        foreach (var relativePath in policyFiles)
        {
            string text;
            try
            {
                var bytes = File.ReadAllBytes(ToPlatformPath(root, relativePath));
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or DecoderFallbackException)
            {
                violations.Add(new SourcePolicyViolation(
                    relativePath,
                    1,
                    $"unreadable UTF-8 text: {SingleLine(exception.Message)}"));
                continue;
            }

            violations.AddRange(TextViolations(relativePath, text));
            if (Path.GetExtension(relativePath).Equals(".py", StringComparison.OrdinalIgnoreCase))
            {
                violations.AddRange(PythonViolations(relativePath, text));
            }
        }

        var failures = violations
            .Distinct()
            .OrderBy(violation => violation.Path, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.Message, StringComparer.Ordinal)
            .Select(violation => violation.Render())
            .ToArray();
        return failures.Length == 0
            ? new RepositoryCheckResult(
                "Source policy",
                true,
                $"Source policy check passed for {policyFiles.Count} active text files.",
                [])
            : Failed(failures);
    }

    public static IReadOnlyList<string> PolicyFiles(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relativePath in RootFiles.Concat(SupportingFiles))
        {
            if (File.Exists(ToPlatformPath(root, relativePath)))
            {
                candidates.Add(relativePath);
            }
        }

        foreach (var scanRoot in ScanRoots)
        {
            var absoluteScanRoot = Path.Combine(root, scanRoot);
            if (!Directory.Exists(absoluteScanRoot))
            {
                continue;
            }

            foreach (var absolutePath in EnumerateFiles(absoluteScanRoot))
            {
                var relativePath = RelativePath(root, absolutePath);
                if (!IsExcluded(relativePath) && TextSuffixes.Contains(Path.GetExtension(relativePath)))
                {
                    candidates.Add(relativePath);
                }
            }
        }

        return candidates.Order(StringComparer.Ordinal).ToArray();
    }

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new("Source policy", false, string.Empty, failures);

    private static IEnumerable<SourcePolicyViolation> CredentialViolations(string repositoryRoot)
    {
        foreach (var absolutePath in EnumerateFiles(repositoryRoot))
        {
            var relativePath = RelativePath(repositoryRoot, absolutePath);
            if (IsExcluded(relativePath))
            {
                continue;
            }

            var fileName = Path.GetFileName(relativePath);
            var forbiddenName = fileName.Equals(".env", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("id_rsa", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("id_rsa.", StringComparison.OrdinalIgnoreCase);
            if (forbiddenName || ForbiddenCredentialSuffixes.Contains(Path.GetExtension(fileName)))
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    1,
                    "credential or signing material is forbidden");
            }
        }
    }

    private static IEnumerable<SourcePolicyViolation> TextViolations(string relativePath, string text)
    {
        var lines = SplitLines(text);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            if (line.Contains('\u2014'))
            {
                yield return new SourcePolicyViolation(relativePath, lineNumber, "em dash is forbidden");
            }

            if (line.EnumerateRunes().Any(IsEmoji))
            {
                yield return new SourcePolicyViolation(relativePath, lineNumber, "emoji is forbidden");
            }

            if (!MarkerExemptions.Contains(relativePath) && MarkerPattern.IsMatch(line))
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    lineNumber,
                    "unfinished-work marker is forbidden");
            }

            if (AttributionPattern.IsMatch(line))
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    lineNumber,
                    "assistant attribution is forbidden");
            }
        }
    }

    private static IEnumerable<SourcePolicyViolation> PythonViolations(string relativePath, string text)
    {
        var lexical = PythonLex(text);
        if (lexical.Error is not null)
        {
            yield return new SourcePolicyViolation(
                relativePath,
                lexical.ErrorLine,
                $"invalid Python lexical structure: {lexical.Error}");
            yield break;
        }

        var statements = PythonStatements(lexical.Tokens);
        foreach (var statement in statements)
        {
            foreach (var token in statement.Where(token => token.Value == "pass"))
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    token.Line,
                    "empty pass statement is forbidden");
            }

            var bareExcept = FindBareExcept(statement);
            if (bareExcept is not null)
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    bareExcept.Line,
                    "bare except clause is forbidden");
            }

            var constantAssertion = FindConstantTrueAssertion(statement);
            if (constantAssertion is not null)
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    constantAssertion.Line,
                    "constant-true assertion is forbidden");
            }

            var ellipsis = FindEllipsisStatement(statement);
            if (ellipsis is not null)
            {
                yield return new SourcePolicyViolation(
                    relativePath,
                    ellipsis.Line,
                    "ellipsis placeholder is forbidden");
            }
        }
    }

    private static PythonToken? FindBareExcept(IReadOnlyList<PythonToken> statement)
    {
        for (var index = 0; index + 1 < statement.Count; index++)
        {
            if (statement[index].Value == "except" && statement[index + 1].Value == ":")
            {
                return statement[index];
            }
        }

        return null;
    }

    private static PythonToken? FindConstantTrueAssertion(IReadOnlyList<PythonToken> statement)
    {
        for (var index = 0; index < statement.Count; index++)
        {
            if (statement[index].Value != "assert")
            {
                continue;
            }

            var cursor = index + 1;
            var openParentheses = 0;
            while (cursor < statement.Count && statement[cursor].Value == "(")
            {
                openParentheses++;
                cursor++;
            }

            if (cursor >= statement.Count || statement[cursor].Value != "True")
            {
                continue;
            }

            var assertion = statement[cursor];
            cursor++;
            while (openParentheses > 0 && cursor < statement.Count && statement[cursor].Value == ")")
            {
                openParentheses--;
                cursor++;
            }

            if (openParentheses == 0 && (cursor == statement.Count || statement[cursor].Value == ","))
            {
                return assertion;
            }
        }

        return null;
    }

    private static PythonToken? FindEllipsisStatement(IReadOnlyList<PythonToken> statement)
    {
        var stripped = StripWrappingParentheses(statement);
        if (stripped.Length == 1 && stripped[0].Value == "...")
        {
            return stripped[0];
        }

        var depth = 0;
        for (var index = 0; index < statement.Count; index++)
        {
            depth = UpdatedDepth(depth, statement[index].Value);
            if (depth != 0 || statement[index].Value != ":")
            {
                continue;
            }

            var prefix = statement.Take(index).ToArray();
            var suffix = StripWrappingParentheses(statement.Skip(index + 1).ToArray());
            if (prefix.Length > 0
                && CompoundStatementKeywords.Contains(prefix[0].Value)
                && suffix.Length == 1
                && suffix[0].Value == "...")
            {
                return suffix[0];
            }
        }

        return null;
    }

    private static readonly HashSet<string> CompoundStatementKeywords = new(
        [
            "async",
            "case",
            "class",
            "def",
            "elif",
            "else",
            "except",
            "finally",
            "for",
            "if",
            "match",
            "try",
            "while",
            "with",
        ],
        StringComparer.Ordinal);

    private static PythonToken[] StripWrappingParentheses(
        IReadOnlyList<PythonToken> tokens)
    {
        var start = 0;
        var end = tokens.Count;
        while (end - start >= 2
            && tokens[start].Value == "("
            && tokens[end - 1].Value == ")"
            && ParenthesesWrap(tokens, start, end))
        {
            start++;
            end--;
        }

        return tokens.Skip(start).Take(end - start).ToArray();
    }

    private static bool ParenthesesWrap(IReadOnlyList<PythonToken> tokens, int start, int end)
    {
        var depth = 0;
        for (var index = start; index < end; index++)
        {
            depth = UpdatedDepth(depth, tokens[index].Value);
            if (depth == 0 && index < end - 1)
            {
                return false;
            }
        }

        return depth == 0;
    }

    private static List<IReadOnlyList<PythonToken>> PythonStatements(
        IReadOnlyList<PythonToken> tokens)
    {
        var statements = new List<IReadOnlyList<PythonToken>>();
        var current = new List<PythonToken>();
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Value == "\n")
            {
                if (depth == 0 && (current.Count == 0 || current[^1].Value != "\\"))
                {
                    AddStatement(statements, current);
                }

                continue;
            }

            if (token.Value == ";" && depth == 0)
            {
                AddStatement(statements, current);
                continue;
            }

            current.Add(token);
            depth = UpdatedDepth(depth, token.Value);
        }

        AddStatement(statements, current);
        return statements;
    }

    private static void AddStatement(
        List<IReadOnlyList<PythonToken>> statements,
        List<PythonToken> current)
    {
        if (current.Count > 0)
        {
            statements.Add(current.ToArray());
            current.Clear();
        }
    }

    private static int UpdatedDepth(int depth, string token) => token switch
    {
        "(" or "[" or "{" => depth + 1,
        ")" or "]" or "}" => Math.Max(0, depth - 1),
        _ => depth,
    };

    private static PythonLexResult PythonLex(string text)
    {
        var tokens = new List<PythonToken>();
        var line = 1;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '\r')
            {
                index++;
                continue;
            }

            if (character == '\n')
            {
                tokens.Add(new PythonToken("\n", line));
                line++;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (character == '#')
            {
                while (index < text.Length && text[index] is not ('\r' or '\n'))
                {
                    index++;
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                var stringLine = line;
                var delimiter = character;
                var triple = index + 2 < text.Length
                    && text[index + 1] == delimiter
                    && text[index + 2] == delimiter;
                index += triple ? 3 : 1;
                var closed = false;
                while (index < text.Length)
                {
                    if (text[index] == '\n')
                    {
                        if (!triple && (index == 0 || text[index - 1] != '\\'))
                        {
                            break;
                        }

                        line++;
                        index++;
                        continue;
                    }

                    if (text[index] == '\\')
                    {
                        index = Math.Min(text.Length, index + 2);
                        continue;
                    }

                    if (text[index] == delimiter
                        && (!triple
                            || (index + 2 < text.Length
                                && text[index + 1] == delimiter
                                && text[index + 2] == delimiter)))
                    {
                        index += triple ? 3 : 1;
                        closed = true;
                        break;
                    }

                    index++;
                }

                if (!closed)
                {
                    return new PythonLexResult([], stringLine, "unterminated string literal");
                }

                continue;
            }

            if (index + 2 < text.Length && text.AsSpan(index, 3).SequenceEqual("..."))
            {
                tokens.Add(new PythonToken("...", line));
                index += 3;
                continue;
            }

            if (character == '_' || char.IsLetter(character))
            {
                var start = index;
                index++;
                while (index < text.Length && (text[index] == '_' || char.IsLetterOrDigit(text[index])))
                {
                    index++;
                }

                tokens.Add(new PythonToken(text[start..index], line));
                continue;
            }

            tokens.Add(new PythonToken(character.ToString(), line));
            index++;
        }

        var delimiterFailure = ValidatePythonDelimiters(tokens);
        return delimiterFailure is null
            ? new PythonLexResult(tokens, 0, null)
            : new PythonLexResult([], delimiterFailure.Value.Line, "unbalanced delimiters");
    }

    private static (int Line, string Token)? ValidatePythonDelimiters(
        IReadOnlyList<PythonToken> tokens)
    {
        var delimiters = new Stack<PythonToken>();
        foreach (var token in tokens)
        {
            if (token.Value is "(" or "[" or "{")
            {
                delimiters.Push(token);
                continue;
            }

            if (token.Value is not (")" or "]" or "}"))
            {
                continue;
            }

            if (delimiters.Count == 0 || !DelimitersMatch(delimiters.Peek().Value, token.Value))
            {
                return (token.Line, token.Value);
            }

            delimiters.Pop();
        }

        return delimiters.Count == 0
            ? null
            : (delimiters.Peek().Line, delimiters.Peek().Value);
    }

    private static bool DelimitersMatch(string opening, string closing) =>
        (opening, closing) is ("(", ")") or ("[", "]") or ("{", "}");

    private static string[] SplitLines(string text) =>
        Regex.Split(text, "\\r\\n|\\r|\\n|\\u0085|\\u2028|\\u2029");

    private static bool IsEmoji(Rune rune)
    {
        var codePoint = rune.Value;
        return codePoint is >= 0x1F1E6 and <= 0x1F1FF
            or >= 0x1F300 and <= 0x1FAFF
            or 0x200D
            or 0x20E3
            or 0xFE0F;
    }

    private static bool IsExcluded(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Any(ExcludedParts.Contains))
        {
            return true;
        }

        return parts.Length >= 2
            && parts[0] == "docs"
            && parts[1] is "archive" or "research";
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var directories = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    yield return entry;
                    continue;
                }

                var relativePath = RelativePath(root, entry);
                if ((attributes & FileAttributes.ReparsePoint) == 0 && !IsExcluded(relativePath))
                {
                    directories.Add(entry);
                }
            }

            for (var index = directories.Count - 1; index >= 0; index--)
            {
                pending.Push(directories[index]);
            }
        }
    }

    private static Regex BuildMarkerPattern()
    {
        var markers = new[]
        {
            string.Concat("TO", "DO"),
            string.Concat("FIX", "ME"),
            string.Concat("HA", "CK"),
            string.Concat("X", "XX"),
        };
        return new Regex(
            $@"\b(?:{string.Join('|', markers.Select(Regex.Escape))})\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static Regex BuildAttributionPattern()
    {
        var names = new[]
        {
            string.Concat("co", "dex"),
            string.Concat("clau", "de"),
            string.Concat("gr", "ok"),
        };
        var namePattern = string.Join('|', names.Select(Regex.Escape));
        var coauthor = string.Concat("co", "-?", "authored", "-by");
        return new Regex(
            $@"\b(?:(?:generated|created|written|authored)\s+by\s+(?:{namePattern})|by\s+(?:{namePattern})|{coauthor}\s*:\s*.*(?:{namePattern}))\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string ToPlatformPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record PythonToken(string Value, int Line);

    private sealed record PythonLexResult(
        IReadOnlyList<PythonToken> Tokens,
        int ErrorLine,
        string? Error);
}
