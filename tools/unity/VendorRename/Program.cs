using Mono.Cecil;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: VendorRename <publish-directory> <output-directory> <clashing-assembly-name>...");
    return 2;
}

var source = Path.GetFullPath(args[0]);
var destination = Path.GetFullPath(args[1]);
var clashes = new HashSet<string>(args.Skip(2), StringComparer.Ordinal);
var assemblies = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
using var resolver = new VendorResolver(assemblies, source);
try
{
    foreach (var path in Directory.GetFiles(source, "*.dll").Order(StringComparer.Ordinal))
    {
        var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, AssemblyResolver = resolver });
        assemblies.Add(assembly.Name.Name, assembly);
    }
    foreach (var name in clashes)
        if (!assemblies.ContainsKey(name)) throw new InvalidOperationException($"Clashing assembly is missing: {name}");

    // Serialized System.Type and enum values inside attribute blobs carry assembly
    // identities independently of the AssemblyRef table. Decode them before
    // renaming, then rewrite their scopes so Cecil emits the new identities.
    void RewriteReference(AssemblyNameReference reference)
    {
        if (!assemblies.TryGetValue(reference.Name, out var target)) return;
        // A publish may resolve a higher version than a dependency requested.
        // Keep every rewritten reference aligned with the actual copied DLL.
        reference.Version = target.Name.Version;
        if (clashes.Contains(reference.Name)) reference.Name = "Sensei.Vendor." + reference.Name;
        reference.PublicKey = Array.Empty<byte>();
        reference.PublicKeyToken = Array.Empty<byte>();
        reference.HasPublicKey = false;
    }

    void RewriteType(TypeReference type)
    {
        if (type.Scope is AssemblyNameReference scope) RewriteReference(scope);
        if (type is TypeSpecification specification) RewriteType(specification.ElementType);
        if (type is GenericInstanceType generic)
            foreach (var argument in generic.GenericArguments) RewriteType(argument);
        if (type.DeclaringType != null) RewriteType(type.DeclaringType);
    }

    void RewriteArgument(CustomAttributeArgument argument)
    {
        RewriteType(argument.Type);
        if (argument.Value is TypeReference type) RewriteType(type);
        if (argument.Value is CustomAttributeArgument nested) RewriteArgument(nested);
        if (argument.Value is CustomAttributeArgument[] array)
            foreach (var item in array) RewriteArgument(item);
    }

    void RewriteAttributes(ICustomAttributeProvider provider)
    {
        if (!provider.HasCustomAttributes) return;
        foreach (var attribute in provider.CustomAttributes)
        {
            foreach (var argument in attribute.ConstructorArguments) RewriteArgument(argument);
            foreach (var field in attribute.Fields) RewriteArgument(field.Argument);
            foreach (var property in attribute.Properties) RewriteArgument(property.Argument);
            if (!attribute.IsResolved)
                throw new InvalidOperationException($"Could not decode attribute {attribute.AttributeType.FullName}");
        }
    }

    void RewriteGenericParameters(IGenericParameterProvider provider)
    {
        if (!provider.HasGenericParameters) return;
        foreach (var parameter in provider.GenericParameters)
        {
            RewriteAttributes(parameter);
            foreach (var constraint in parameter.Constraints) RewriteAttributes(constraint);
        }
    }

    void RewriteTypeAttributes(TypeDefinition type)
    {
        RewriteAttributes(type);
        RewriteGenericParameters(type);
        foreach (var iface in type.Interfaces) RewriteAttributes(iface);
        foreach (var field in type.Fields) RewriteAttributes(field);
        foreach (var property in type.Properties) RewriteAttributes(property);
        foreach (var evt in type.Events) RewriteAttributes(evt);
        foreach (var method in type.Methods)
        {
            RewriteAttributes(method);
            RewriteAttributes(method.MethodReturnType);
            RewriteGenericParameters(method);
            foreach (var parameter in method.Parameters) RewriteAttributes(parameter);
        }
        foreach (var nested in type.NestedTypes) RewriteTypeAttributes(nested);
    }

    foreach (var assembly in assemblies.Values)
    {
        RewriteAttributes(assembly);
        foreach (var module in assembly.Modules)
        {
            RewriteAttributes(module);
            foreach (var type in module.Types) RewriteTypeAttributes(type);
        }
    }

    Directory.CreateDirectory(destination);
    foreach (var (originalName, assembly) in assemblies)
    {
        // Orchestrator decision 2026-10-28: keep engine source intact and isolate Unity's conflicting assemblies by identity.
        if (clashes.Contains(originalName)) assembly.Name.Name = "Sensei.Vendor." + originalName;
        assembly.Name.PublicKey = Array.Empty<byte>();
        assembly.Name.HasPublicKey = false;
        assembly.MainModule.Attributes &= ~ModuleAttributes.StrongNameSigned;

        foreach (var module in assembly.Modules)
        foreach (var reference in module.AssemblyReferences)
        {
            RewriteReference(reference);
        }

        assembly.Write(Path.Combine(destination, assembly.Name.Name + ".dll"));
        Console.WriteLine($"{originalName} -> {assembly.Name.Name}");
    }
    // Refuse to ship a half-rewritten closure or an invalidated strong name.
    var outputNames = assemblies.Values.ToDictionary(a => a.Name.Name, a => a.Name.Version, StringComparer.Ordinal);
    foreach (var (name, version) in outputNames)
    {
        using var output = AssemblyDefinition.ReadAssembly(Path.Combine(destination, name + ".dll"));
        if (output.Name.Name != name || output.Name.Version != version || output.Name.HasPublicKey ||
            output.MainModule.Attributes.HasFlag(ModuleAttributes.StrongNameSigned))
            throw new InvalidOperationException($"Invalid rewritten assembly identity: {name}");
        foreach (var reference in output.MainModule.AssemblyReferences)
        {
            if (clashes.Contains(reference.Name))
                throw new InvalidOperationException($"Unrewritten reference in {name}: {reference.FullName}");
            if (outputNames.TryGetValue(reference.Name, out var targetVersion) &&
                (reference.Version != targetVersion || reference.HasPublicKey || reference.PublicKeyToken.Length != 0))
                throw new InvalidOperationException($"Invalid reference in {name}: {reference.FullName}");
        }
    }
    return 0;
}
finally
{
    foreach (var assembly in assemblies.Values) assembly.Dispose();
}

sealed class VendorResolver : IAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> assemblies;
    private readonly DefaultAssemblyResolver fallback = new();

    public VendorResolver(Dictionary<string, AssemblyDefinition> assemblies, string source)
    {
        this.assemblies = assemblies;
        fallback.AddSearchDirectory(source);
        fallback.AddSearchDirectory(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
    }

    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());
    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        var key = name.Name.StartsWith("Sensei.Vendor.", StringComparison.Ordinal)
            ? name.Name.Substring("Sensei.Vendor.".Length) : name.Name;
        return assemblies.TryGetValue(key, out var assembly) ? assembly : fallback.Resolve(name, parameters);
    }
    public void Dispose() => fallback.Dispose();
}
