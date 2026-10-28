using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit.Abstractions;

namespace AgentSpeed.ProfileHarness;

internal static class NativeTests
{
    public static bool Run(string output, string? filter)
    {
        var results = new List<object>();
        var log = new List<string>();
        var failed = false;
        foreach (var type in typeof(Windows.Tests.ProfileAvatarTests).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested && t.Namespace == "AgentSpeed.Windows.Tests"))
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute")))
        {
            var name = type.Name + "." + method.Name;
            if (filter is not null && !name.Contains(filter, StringComparison.Ordinal)) continue;
            string? error = null;
            try
            {
                var constructor = type.GetConstructors().Single();
                var instance = constructor.GetParameters().Length == 0 ? Activator.CreateInstance(type)
                    : Activator.CreateInstance(type, new TestOutput(log));
                if (method.Invoke(instance, null) is Task task) task.GetAwaiter().GetResult();
            }
            catch (Exception exception) { error = exception.GetBaseException().Message; failed = true; }
            results.Add(new { name, passed = error is null, error });
        }
        File.WriteAllText(Path.Combine(output, "native-tests.json"), JsonSerializer.Serialize(new { passed = !failed, count = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(output, "native-tests.log"), log);
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "title-widths.tsv")))
            File.Copy(Path.Combine(AppContext.BaseDirectory, "title-widths.tsv"), Path.Combine(output, "title-widths.tsv"), overwrite: true);
        return !failed && results.Count > 0;
    }
    private sealed class TestOutput(List<string> lines) : ITestOutputHelper
    {
        public void WriteLine(string message) => lines.Add(message);
        public void WriteLine(string format, params object[] args) => lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
    }
}
