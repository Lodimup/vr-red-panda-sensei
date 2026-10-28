using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sensei.Engine.Json;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Foundation;

public sealed class SourceTreeTests
{
    // SPEC S10 order.
    private static readonly string[] SpecToolNames =
    {
        "inspect_scene", "spawn_objects", "move_object", "remove_objects", "highlight_objects", "animate_sensei",
        "speak", "start_challenge", "update_challenge", "show_hint", "set_support", "record_attempt",
        "end_activity", "query_learner", "replay_speech", "remember_note",
    };

    private static readonly Lazy<string> LibraryDirectory = new(FindLibraryDirectory);

    private static string FindLibraryDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var viaRoot = Path.Combine(dir.FullName, "sensei-engine", "src", "Sensei.Engine");
            if (File.Exists(Path.Combine(viaRoot, "Sensei.Engine.csproj"))) return viaRoot;
            var viaEngine = Path.Combine(dir.FullName, "src", "Sensei.Engine");
            if (File.Exists(Path.Combine(viaEngine, "Sensei.Engine.csproj"))) return viaEngine;
        }
        throw new DirectoryNotFoundException("sensei-engine/src/Sensei.Engine not found above " + AppContext.BaseDirectory);
    }

    private static string CsprojPath => Path.Combine(LibraryDirectory.Value, "Sensei.Engine.csproj");

    /// <summary>Every hand-written .cs file of the library (bin/obj excluded).</summary>
    private static IReadOnlyList<(string Path, string Text)> SourceFiles()
    {
        var root = LibraryDirectory.Value;
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(root, p).Replace('\\', '/');
                return !rel.StartsWith("obj/", StringComparison.Ordinal) && !rel.StartsWith("bin/", StringComparison.Ordinal);
            })
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (p, File.ReadAllText(p)))
            .ToArray();
    }

    /// <summary>The source with whole-line // comments (including /// doc comments) blanked, so prose never trips a rule.</summary>
    private static string WithoutCommentLines(string source) =>
        string.Join("\n", source.Split('\n').Select(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : l));

    private static string Rel(string path) => Path.GetRelativePath(LibraryDirectory.Value, path);

    // ------------------------------------------------------------------ locating the tree

    [Fact]
    public void S1_library_source_tree_is_found_and_not_empty()
    {
        Assert.True(File.Exists(CsprojPath));
        Assert.True(SourceFiles().Count >= 30, "expected the library to have many source files, found " + SourceFiles().Count);
        Assert.Contains(SourceFiles(), f => f.Path.EndsWith("SenseiJsonContext.cs", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ S2

    private static IReadOnlyList<XElement> PackageReferences() =>
        XDocument.Load(CsprojPath).Descendants("PackageReference").ToArray();

    [Theory]
    [InlineData("Microsoft.Agents.AI")]
    [InlineData("Microsoft.Agents.AI.OpenAI")]
    public void S2_library_references_the_agent_framework_package_at_1_26_0(string package)
    {
        var reference = Assert.Single(PackageReferences(), r => (string?)r.Attribute("Include") == package);
        Assert.Equal("1.26.0", (string?)reference.Attribute("Version"));
    }

    [Fact]
    public void S2_library_targets_netstandard2_1()
    {
        var targets = XDocument.Load(CsprojPath).Descendants("TargetFramework").Select(e => e.Value.Trim()).ToArray();
        Assert.Equal(new[] { "netstandard2.1" }, targets);
        Assert.Empty(XDocument.Load(CsprojPath).Descendants("TargetFrameworks"));
    }

    [Fact]
    public void S2_library_project_does_not_reference_a_unity_package_or_project()
    {
        var csproj = File.ReadAllText(CsprojPath);
        Assert.DoesNotContain("Unity", csproj, StringComparison.OrdinalIgnoreCase);
        foreach (var r in PackageReferences())
            Assert.DoesNotContain("Unity", (string?)r.Attribute("Include") ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Empty(XDocument.Load(CsprojPath).Descendants("ProjectReference"));
    }

    [Fact]
    public void S2_no_library_source_file_references_UnityEngine()
    {
        var offenders = SourceFiles().Where(f => f.Text.Contains("UnityEngine", StringComparison.Ordinal)).Select(f => Rel(f.Path)).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void S2_library_assembly_references_no_unity_assembly()
    {
        var names = typeof(SenseiEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n => n.StartsWith("UnityEngine", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.StartsWith("Unity.", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ S3: banned constructs

    [Fact]
    public void S3_no_tool_is_created_with_AIFunctionFactory_Create()
    {
        var rx = new Regex(@"AIFunctionFactory\s*\.\s*Create");
        var offenders = SourceFiles().Where(f => rx.IsMatch(WithoutCommentLines(f.Text))).Select(f => Rel(f.Path)).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void S3_no_dynamic_keyword_is_used()
    {
        var offenders = SourceFiles().Where(f => FindDynamicUsage(f.Text)).Select(f => Rel(f.Path)).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void S3_no_Reflection_Emit_is_used()
    {
        var offenders = SourceFiles().Where(f => WithoutCommentLines(f.Text).Contains("Reflection.Emit", StringComparison.Ordinal)).Select(f => Rel(f.Path)).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void S3_no_Activator_CreateInstance_is_used()
    {
        var rx = new Regex(@"Activator\s*\.\s*CreateInstance");
        var offenders = SourceFiles().Where(f => rx.IsMatch(WithoutCommentLines(f.Text))).Select(f => Rel(f.Path)).ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public void S3_dynamic_detector_flags_real_usages_and_ignores_prose_and_identifiers()
    {
        Assert.True(FindDynamicUsage("dynamic x = 1;"));
        Assert.True(FindDynamicUsage("    var y = (dynamic )z;"));
        Assert.True(FindDynamicUsage("public dynamic Get() => 1;"));
        Assert.False(FindDynamicUsage("/// a dynamic value\n// dynamic thing"));
        Assert.False(FindDynamicUsage("var isDynamicLevel = 1; var dynamicRange = 2;"));
    }

    private static bool FindDynamicUsage(string source) =>
        Regex.IsMatch(WithoutCommentLines(source), @"(?<![\w.@])dynamic\s");

    // ------------------------------------------------------------------ S3: JSON serializer calls carry type info

    private static readonly Regex SerializerCall =
        new(@"\.\s*(?<method>(?:De)?[Ss]erialize\w*)\s*(?<generic><[^()]*?>)?\s*\(", RegexOptions.CultureInvariant);

    private static readonly Regex TypeInfoParameter =
        new(@"JsonTypeInfo<(?:[^<>]|<[^<>]*>)+>\s+(?<name>\w+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Violations of S3 in <paramref name="source"/>: any Serialize*/Deserialize* call that names a generic argument,
    /// mentions <c>typeof(</c>, or passes no <c>JsonTypeInfo</c> (a <c>SenseiJsonContext.Default.X</c> property or a
    /// declared <c>JsonTypeInfo&lt;T&gt;</c> parameter). Returns one description per violation; also counts calls inspected.
    /// </summary>
    private static (List<string> Violations, int Calls) InspectSerializerCalls(string source)
    {
        var text = WithoutCommentLines(source);
        var typeInfoNames = TypeInfoParameter.Matches(text).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();
        var calls = 0;
        foreach (Match m in SerializerCall.Matches(text))
        {
            // Only JSON serializer calls: receiver is JsonSerializer or a JsonElement/JsonNode style extension.
            var prefix = text.Substring(Math.Max(0, m.Index - 20), Math.Min(20, m.Index));
            var isSerializer = prefix.TrimEnd().EndsWith("JsonSerializer", StringComparison.Ordinal);
            var method = m.Groups["method"].Value;
            if (!isSerializer && method != "Deserialize" && method != "DeserializeAsync") continue;
            calls++;

            var args = ReadArguments(text, m.Index + m.Length);
            var callText = m.Value + args.Raw + ")";
            var label = method + "(" + string.Join(", ", args.Parts.Select(p => p.Trim())) + ")";

            if (m.Groups["generic"].Success) violations.Add("explicit generic argument: " + label);
            if (callText.Contains("typeof(", StringComparison.Ordinal)) violations.Add("typeof inside call: " + label);
            if (!args.Parts.Any(p => IsTypeInfoArgument(p.Trim(), typeInfoNames))) violations.Add("no JsonTypeInfo argument: " + label);
        }
        return (violations, calls);
    }

    private static bool IsTypeInfoArgument(string arg, HashSet<string> typeInfoNames) =>
        Regex.IsMatch(arg, @"^(?:[\w.]+\.)?SenseiJsonContext\.Default\.\w+$") || typeInfoNames.Contains(arg);

    private static (string Raw, List<string> Parts) ReadArguments(string text, int start)
    {
        var depth = 1;
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var i = start;
        for (; i < text.Length && depth > 0; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                current.Append(c);
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\') { current.Append(text[i]); i++; }
                    if (i < text.Length) current.Append(text[i]);
                    i++;
                }
                if (i < text.Length) current.Append(text[i]);
                continue;
            }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}')
            {
                depth--;
                if (depth == 0) break;
            }
            if (c == ',' && depth == 1)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.ToString().Trim().Length > 0) parts.Add(current.ToString());
        return (text.Substring(start, Math.Max(0, i - start)), parts);
    }

    [Fact]
    public void S3_every_JsonSerializer_call_in_the_library_passes_type_info()
    {
        var all = new List<string>();
        var total = 0;
        foreach (var f in SourceFiles())
        {
            var (violations, calls) = InspectSerializerCalls(f.Text);
            total += calls;
            all.AddRange(violations.Select(v => Rel(f.Path) + ": " + v));
        }

        Assert.Empty(all);
        // Vacuity guard: the library really has serializer calls and the scan really saw them.
        Assert.True(total >= 5, "expected at least 5 serializer calls to inspect, saw " + total);
    }

    [Fact]
    public void S3_serializer_call_scan_sees_the_known_calls_in_storage_and_json_copy()
    {
        var files = SourceFiles().ToDictionary(f => Rel(f.Path).Replace('\\', '/'), f => f.Text);
        Assert.True(InspectSerializerCalls(files["Storage/FileProfileStore.cs"].ToString()).Calls >= 2);
        Assert.True(InspectSerializerCalls(files["Json/JsonCopy.cs"].ToString()).Calls >= 2);
    }

    [Theory]
    [InlineData("var s = JsonSerializer.Serialize<Foo>(x, SenseiJsonContext.Default.Foo);", "explicit generic")]
    [InlineData("var s = JsonSerializer.Deserialize<Foo>(json, SenseiJsonContext.Default.Foo);", "explicit generic")]
    [InlineData("var s = JsonSerializer.Deserialize(json, typeof(Foo));", "typeof")]
    [InlineData("var s = JsonSerializer.Deserialize(json, typeof(Foo), SenseiJsonContext.Default.Foo);", "typeof")]
    [InlineData("var s = JsonSerializer.Serialize(x);", "no JsonTypeInfo")]
    [InlineData("var s = JsonSerializer.Serialize(x, options);", "no JsonTypeInfo")]
    [InlineData("var s = JsonSerializer.Serialize(x, SenseiJsonContext.Default);", "no JsonTypeInfo")]
    [InlineData("var s = JsonSerializer.SerializeToUtf8Bytes(x, JsonSerializerOptions.Default);", "no JsonTypeInfo")]
    [InlineData("var s = element.Deserialize<Foo>();", "explicit generic")]
    [InlineData("var s = element.Deserialize(typeof(Foo));", "typeof")]
    [InlineData("var s = JsonSerializer\n    .Serialize(\n        x,\n        options);", "no JsonTypeInfo")]
    public void S3_serializer_call_scan_flags_calls_without_type_info(string source, string expectedKind)
    {
        var (violations, calls) = InspectSerializerCalls(source);
        Assert.True(calls >= 1);
        Assert.Contains(violations, v => v.Contains(expectedKind, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("var s = JsonSerializer.Serialize(x, SenseiJsonContext.Default.Foo);")]
    [InlineData("var s = JsonSerializer.Deserialize(bytes, Sensei.Engine.Json.SenseiJsonContext.Default.LearnerProfile);")]
    [InlineData("var s = JsonSerializer.SerializeToUtf8Bytes(\n    value,\n    SenseiJsonContext.Default.Foo);")]
    [InlineData("void M(JsonTypeInfo<Foo> info) { JsonSerializer.SerializeToElement(value, info); }")]
    [InlineData("void M(JsonTypeInfo<List<Foo>> typeInfo) { var s = element.Deserialize(typeInfo); }")]
    [InlineData("// JsonSerializer.Serialize<Foo>(x)\n/// JsonSerializer.Deserialize(json, typeof(Foo))\nvar a = 1;")]
    public void S3_serializer_call_scan_accepts_calls_with_type_info(string source)
    {
        var (violations, _) = InspectSerializerCalls(source);
        Assert.Empty(violations);
    }

    // ------------------------------------------------------------------ S3: one JSON context

    [Fact]
    public void S3_exactly_one_class_in_the_library_source_derives_from_JsonSerializerContext()
    {
        var rx = new Regex(@"\bclass\s+(?<name>\w+)\s*:\s*(?:[\w.]+\.)?JsonSerializerContext\b");
        var found = SourceFiles()
            .SelectMany(f => rx.Matches(WithoutCommentLines(f.Text)).Select(m => m.Groups["name"].Value))
            .ToArray();
        Assert.Equal(new[] { "SenseiJsonContext" }, found);
    }

    [Fact]
    public void S3_exactly_one_type_in_the_library_assembly_derives_from_JsonSerializerContext()
    {
        var types = typeof(SenseiEngine).Assembly.GetTypes().Where(t => typeof(JsonSerializerContext).IsAssignableFrom(t)).ToArray();
        var only = Assert.Single(types);
        Assert.Equal(typeof(SenseiJsonContext), only);
        Assert.Equal("SenseiJsonContext", only.Name);
    }

    // ------------------------------------------------------------------ S3: tools are AIFunction subclasses with checked-in schemas

    private static IReadOnlyList<Type> ToolTypesInAssembly() =>
        typeof(SenseiTool).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(SenseiTool).IsAssignableFrom(t)).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

    [Fact]
    public void S3_the_library_has_exactly_the_16_standard_tool_classes()
    {
        var standard = ToolRegistry.CreateStandardTools().Select(t => t.GetType()).OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(16, standard.Length);
        Assert.Equal(standard, ToolTypesInAssembly());
    }

    [Fact]
    public void S3_every_tool_is_an_AIFunction_subclass()
    {
        foreach (var t in ToolTypesInAssembly())
            Assert.True(typeof(Microsoft.Extensions.AI.AIFunction).IsAssignableFrom(t), t.Name);
    }

    [Fact]
    public void S3_every_tool_source_file_holds_a_raw_string_schema_that_is_the_served_schema()
    {
        var files = SourceFiles();
        var rx = new Regex("SchemaJson\\s*=>\\s*\"\"\"(?<json>.*?)\"\"\"", RegexOptions.Singleline);
        var tools = ToolRegistry.CreateStandardTools();
        Assert.Equal(16, tools.Count);

        foreach (var tool in tools)
        {
            var type = tool.GetType();
            var file = Assert.Single(files, f => Regex.IsMatch(f.Text, @"\bclass\s+" + Regex.Escape(type.Name) + @"\b"));
            var match = rx.Match(file.Text);
            Assert.True(match.Success, type.Name + ": no SchemaJson raw string literal in " + Rel(file.Path));

            var literal = JsonNode.Parse(match.Groups["json"].Value)!;
            var served = JsonNode.Parse(tool.JsonSchema.GetRawText())!;
            Assert.True(JsonNode.DeepEquals(literal, served), type.Name + ": served JsonSchema differs from the checked-in literal");
        }
    }

    [Fact]
    public void S3_every_tool_schema_is_an_object_with_additionalProperties_false()
    {
        foreach (var tool in ToolRegistry.CreateStandardTools())
        {
            var schema = tool.JsonSchema;
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.True(schema.TryGetProperty("type", out var type), tool.Name + ": no type");
            Assert.Equal("object", type.GetString());
            Assert.True(schema.TryGetProperty("additionalProperties", out var additional), tool.Name + ": no additionalProperties");
            Assert.Equal(JsonValueKind.False, additional.ValueKind);
        }
    }

    [Fact]
    public void S3_every_nested_object_in_a_tool_schema_also_forbids_additional_properties()
    {
        var missing = new List<string>();
        foreach (var tool in ToolRegistry.CreateStandardTools())
            CollectObjectsWithoutClosedProperties(tool.JsonSchema, tool.Name, missing);
        Assert.Empty(missing);
    }

    private static void CollectObjectsWithoutClosedProperties(JsonElement schema, string path, List<string> missing)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;
        if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
        {
            if (!schema.TryGetProperty("additionalProperties", out var ap) || ap.ValueKind != JsonValueKind.False) missing.Add(path);
        }
        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            foreach (var p in props.EnumerateObject()) CollectObjectsWithoutClosedProperties(p.Value, path + "." + p.Name, missing);
        if (schema.TryGetProperty("items", out var items)) CollectObjectsWithoutClosedProperties(items, path + "[]", missing);
    }

    // ------------------------------------------------------------------ S10

    [Fact]
    public void S10_spec_list_has_16_distinct_names()
    {
        Assert.Equal(16, SpecToolNames.Length);
        Assert.Equal(16, SpecToolNames.Distinct().Count());
    }

    [Fact]
    public void S10_ToolRegistry_StandardNames_are_the_16_spec_names_in_order()
    {
        Assert.Equal(SpecToolNames, ToolRegistry.StandardNames);
    }

    [Fact]
    public void S10_standard_tool_instances_carry_the_spec_names_in_order()
    {
        Assert.Equal(SpecToolNames, ToolRegistry.CreateStandardTools().Select(t => t.Name));
    }

    [Fact]
    public async Task S10_a_created_engines_ToolNames_are_the_16_spec_names_in_order()
    {
        await using var te = await TestEngine.CreateAsync();
        Assert.Equal(SpecToolNames, te.Engine.ToolNames);
    }

    [Fact]
    public async Task S10_engine_ToolNames_ignore_extra_test_tools()
    {
        await using var te = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new ProbeTool() });
        Assert.Equal(SpecToolNames, te.Engine.ToolNames);
    }

    // ------------------------------------------------------------------ S112

    [Fact]
    public void S112_every_standard_tool_has_a_non_empty_description_ending_with_a_period()
    {
        foreach (var tool in ToolRegistry.CreateStandardTools())
        {
            var d = tool.Description;
            Assert.False(string.IsNullOrWhiteSpace(d), tool.Name + ": empty description");
            Assert.Equal(d.Trim(), d);
            Assert.EndsWith(".", d, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void S112_tool_descriptions_are_sentences_not_one_word_labels()
    {
        foreach (var tool in ToolRegistry.CreateStandardTools())
            Assert.True(tool.Description.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4, tool.Name + ": " + tool.Description);
    }

    [Fact]
    public void S112_tool_descriptions_are_distinct()
    {
        var descriptions = ToolRegistry.CreateStandardTools().Select(t => t.Description).ToArray();
        Assert.Equal(descriptions.Length, descriptions.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void S112_every_property_at_every_depth_of_every_tool_schema_has_a_non_empty_description()
    {
        var missing = new List<string>();
        var visited = new List<string>();
        foreach (var tool in ToolRegistry.CreateStandardTools())
            WalkProperties(tool.JsonSchema, tool.Name, missing, visited);

        Assert.Empty(missing);
        // Vacuity guards: the walk really descended through properties and items.
        Assert.True(visited.Count >= 40, "expected at least 40 schema properties across the tools, visited " + visited.Count);
        Assert.Contains("speak.segments", visited);
        Assert.Contains("speak.segments[].text", visited);
    }

    [Fact]
    public void S112_every_tool_that_declares_properties_describes_each_of_them()
    {
        foreach (var tool in ToolRegistry.CreateStandardTools())
        {
            if (!tool.JsonSchema.TryGetProperty("properties", out var props)) continue;
            foreach (var p in props.EnumerateObject())
            {
                Assert.True(p.Value.TryGetProperty("description", out var d), tool.Name + "." + p.Name + ": no description");
                Assert.Equal(JsonValueKind.String, d.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(d.GetString()), tool.Name + "." + p.Name);
            }
        }
    }

    [Fact]
    public void S112_schema_walker_flags_missing_empty_and_nested_missing_descriptions()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "ok": { "type": "string", "description": "Fine." },
                "none": { "type": "string" },
                "blank": { "type": "string", "description": "   " },
                "nested": {
                  "type": "object",
                  "description": "Nested.",
                  "properties": { "inner": { "type": "string" }, "good": { "type": "string", "description": "Good." } }
                },
                "list": {
                  "type": "array",
                  "description": "A list.",
                  "items": { "type": "object", "properties": { "deep": { "type": "integer" } } }
                }
              }
            }
            """;
        using var doc = JsonDocument.Parse(schema);
        var missing = new List<string>();
        var visited = new List<string>();

        WalkProperties(doc.RootElement, "t", missing, visited);

        Assert.Equal(new[] { "t.blank", "t.list[].deep", "t.nested.inner", "t.none" }, missing.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains("t.nested.good", visited);
        Assert.Equal(8, visited.Count);
    }

    private static void WalkProperties(JsonElement schema, string path, List<string> missing, List<string> visited)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;
        if (schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in props.EnumerateObject())
            {
                var childPath = path + "." + p.Name;
                visited.Add(childPath);
                var hasDescription = p.Value.ValueKind == JsonValueKind.Object
                                     && p.Value.TryGetProperty("description", out var d)
                                     && d.ValueKind == JsonValueKind.String
                                     && !string.IsNullOrWhiteSpace(d.GetString());
                if (!hasDescription) missing.Add(childPath);
                WalkProperties(p.Value, childPath, missing, visited);
            }
        }
        if (schema.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            WalkProperties(items, path + "[]", missing, visited);
    }
}
