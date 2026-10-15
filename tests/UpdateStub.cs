using System;
using System.IO;
using System.Reflection;
static class UpdateStub
{
    static void Main(string[] args)
    {
        File.WriteAllText(Assembly.GetExecutingAssembly().Location + ".started", string.Join(" ", args));
    }
}
