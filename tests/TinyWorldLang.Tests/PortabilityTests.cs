using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace TinyWorldLang.Tests
{
    public class PortabilityTests
    {
        private static readonly string RepoRoot = FindRepoRoot();
        private static readonly string CoreProject = Path.Combine(RepoRoot, "src", "TinyWorldLang", "TinyWorldLang.csproj");
        private static readonly string CoreSource = Path.Combine(RepoRoot, "src", "TinyWorldLang");

        [Fact]
        public void CoreProject_StaysUnityAndAotFriendly()
        {
            var doc = XDocument.Load(CoreProject);
            var target = doc.Descendants("TargetFramework").Single().Value.Trim();

            Assert.Equal("netstandard2.0", target);
            Assert.Empty(doc.Descendants("PackageReference"));
        }

        [Fact]
        public void CoreSource_DoesNotUseKnownAotHostileApis()
        {
            string[] banned =
            {
                "System.Reflection.Emit",
                "DynamicMethod",
                "Expression.Compile",
                "CSharpCodeProvider",
                "Assembly.Load",
                "dynamic ",
                "System.Threading",
                "System.Timers",
            };

            var hits =
                from file in Directory.GetFiles(CoreSource, "*.cs", SearchOption.AllDirectories)
                where !IsBuildOutput(file)
                let text = File.ReadAllText(file)
                from token in banned
                where text.IndexOf(token, StringComparison.Ordinal) >= 0
                select $"{Path.GetRelativePath(RepoRoot, file)} contains {token}";

            Assert.Empty(hits);
        }

        private static bool IsBuildOutput(string file)
        {
            var relativePath = Path.GetRelativePath(CoreSource, file);
            return relativePath
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part == "bin" || part == "obj");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TinyWorldLang.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("could not find repository root");
        }
    }
}
