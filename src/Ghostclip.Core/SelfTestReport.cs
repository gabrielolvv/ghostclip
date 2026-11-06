using System.Text;

namespace Ghostclip.Core;

/// <summary>One thing the self-test checked.</summary>
public sealed record SelfTestStep(string Name, bool Ok, string? Detail = null);

/// <summary>What the self-test found, as text to paste in a bug report. Paths are taken out: they'd say who uses the PC.</summary>
public static class SelfTestReport
{
    private const int MaxDetail = 300;

    public static string Format(IReadOnlyList<SelfTestStep> steps)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Self-test: {steps.Count(s => s.Ok)}/{steps.Count} passed"));
        foreach (var step in steps)
        {
            text.Append(step.Ok ? "[ok] " : "[FAILED] ").Append(step.Name);
            if (!string.IsNullOrEmpty(step.Detail)) text.Append(": ").Append(step.Detail);
            text.AppendLine();
        }
        return text.ToString();
    }

    public static string? Clean(string? detail, string testFolder, string home)
    {
        if (detail is null) return null;
        var clean = detail
            .Replace(testFolder, "<test folder>", StringComparison.OrdinalIgnoreCase)
            .Replace(home, "<home>", StringComparison.OrdinalIgnoreCase);
        return clean.Length > MaxDetail ? clean[..MaxDetail] : clean;
    }
}
