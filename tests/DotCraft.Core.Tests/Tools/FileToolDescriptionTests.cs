using System.ComponentModel;
using DotCraft.Tools;
using DotCraft.Tools.Sandbox;

namespace DotCraft.Tests.Tools;

public sealed class FileToolDescriptionTests
{
    [Fact]
    public void FileTools_WriteFile_Description_PrefersTargetedEditsForExistingFiles()
    {
        var description = GetDescription(typeof(FileTools), nameof(FileTools.WriteFile));

        Assert.Contains("creating new files or intentional full-file rewrites", description);
        Assert.Contains("prefer EditFile for targeted changes", description);
    }

    [Fact]
    public void FileTools_EditFile_Description_PrefersSmallPreciseReplacements()
    {
        var description = GetDescription(typeof(FileTools), nameof(FileTools.EditFile));

        Assert.Contains("Prefer a minimal unique snippet", description);
        Assert.Contains("prefer targeted EditFile replacements over full-file rewrites", description);
        Assert.Contains("Use replaceAll only when you intentionally want to replace every exact occurrence", description);
    }

    [Fact]
    public void SandboxFileTools_Descriptions_MatchEditingWorkflowGuidance()
    {
        var writeDescription = GetDescription(typeof(SandboxFileTools), nameof(SandboxFileTools.WriteFile));
        var editDescription = GetDescription(typeof(SandboxFileTools), nameof(SandboxFileTools.EditFile));

        Assert.Contains("creating new files or intentional full-file rewrites", writeDescription);
        Assert.Contains("prefer EditFile for targeted changes", writeDescription);
        Assert.Contains("Prefer a minimal unique snippet", editDescription);
        Assert.Contains("prefer targeted EditFile replacements over full-file rewrites", editDescription);
    }

    private static string GetDescription(Type type, string methodName)
    {
        var method = type.GetMethod(methodName);
        Assert.NotNull(method);

        var attribute = method!.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
            .Cast<DescriptionAttribute>()
            .SingleOrDefault();
        Assert.NotNull(attribute);

        return attribute!.Description;
    }
}
