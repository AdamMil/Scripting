using System;
using Scripting.AST;

namespace Scripting.Emit
{

public static class SnippetMaker
{
  public static void DumpAssembly()
  {
    Assembly.Save();
    string name = "snippets" + index.Next;
    Assembly = new AssemblyGenerator(name, name+".dll", true);
  }

  public static Snippet Generate(ASTNode body)
  {
    return Assembly.GenerateSnippet(body);
  }

  public static AssemblyGenerator Assembly = new AssemblyGenerator("snippets", "snippets.dll", true);
  
  static readonly Index index = new Index();
}

} // namespace Scripting.Emit