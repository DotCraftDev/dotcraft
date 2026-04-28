using System.ComponentModel;
using System.Reflection;
using DotCraft.Tools;

namespace DotCraft.Tests.Tools;

public sealed class PlanToolsTests
{
    [Fact]
    public void CreatePlanDescriptions_EncourageCompactPlans()
    {
        var method = typeof(PlanTools).GetMethod(nameof(PlanTools.CreatePlan))!;
        var methodDescription = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
        var parameters = method.GetParameters();
        var planDescription = parameters.Single(p => p.Name == "plan")
            .GetCustomAttribute<DescriptionAttribute>()!.Description;
        var todosDescription = parameters.Single(p => p.Name == "todos")
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Contains("compact decision-complete Markdown", methodDescription);
        Assert.Contains("Include only implementation details needed to remove ambiguity", planDescription);
        Assert.Contains("mention key files only when needed", planDescription);
        Assert.Contains("keep verification in a short test section", planDescription);
        Assert.Contains("3-7 high-level actionable implementation tasks", todosDescription);
        Assert.Contains("Do not include search, reading, or explanation-only steps", todosDescription);

        Assert.DoesNotContain("detailed implementation content", methodDescription);
        Assert.DoesNotContain(
            "Include specific file paths, implementation details, and verification steps",
            planDescription);
    }
}
