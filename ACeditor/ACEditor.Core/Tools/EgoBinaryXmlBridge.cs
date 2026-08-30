using System.Reflection;
using System.Runtime.Loader;
using System.Xml;

namespace ACEditor.Core.Tools;

internal sealed class EgoBinaryXmlBridge
{
    private readonly string? _root;
    public EgoBinaryXmlBridge(string? root) => _root = root;

    public bool IsAvailable => _root is not null && File.Exists(Path.Combine(_root, "EgoEngineLibrary.dll"));

    public XmlDocument Open(string path)
    {
        if (!IsAvailable) throw new InvalidOperationException("EgoEngineLibrary 15.0.0 is not configured.");
        AssemblyLoadContext context = AssemblyLoadContext.Default;
        Assembly? Resolver(AssemblyLoadContext _, AssemblyName name)
        {
            string candidate = Path.Combine(_root!, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        }
        context.Resolving += Resolver;
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(Path.Combine(_root!, "EgoEngineLibrary.dll"));
            Type type = assembly.GetType("EgoEngineLibrary.Xml.XmlFile", throwOnError: true)!;
            using var stream = File.OpenRead(path);
            object xmlFile = Activator.CreateInstance(type, stream)
                             ?? throw new InvalidDataException("Ego XmlFile could not be created.");
            return (XmlDocument)(type.GetProperty("Document", BindingFlags.Public | BindingFlags.Instance)
                                     ?.GetValue(xmlFile)
                                 ?? throw new MissingMemberException(type.FullName, "Document"));
        }
        finally { context.Resolving -= Resolver; }
    }
}
