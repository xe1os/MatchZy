using MatchZy;

namespace MatchZy.Tests;

public class ConfigCfgMergeTests
{
    private const string Template =
        "// Whether pauses are allowed\n" +
        "matchzy_allow_pause true\n" +
        "\n" +
        "// Chat prefix\n" +
        "matchzy_chat_prefix [MatchZy]\n" +
        "\n" +
        "// Hostname format, e.g. matchzy_hostname_format \"{TEAM1} vs {TEAM2}\"\n" +
        "matchzy_hostname_format \"\"\n";

    private static readonly HashSet<string> AllRegistered = new(StringComparer.OrdinalIgnoreCase)
    {
        "matchzy_allow_pause", "matchzy_chat_prefix", "matchzy_hostname_format",
    };

    [Fact]
    public void FileThatHasEverySettingIsLeftUntouched()
    {
        var result = ConfigCfgMerge.Merge(Template, Template, AllRegistered);

        Assert.Null(result.NewContent);
        Assert.Equal(0, result.AppendedCount);
        Assert.Empty(result.RemovedNames);
    }

    [Fact]
    public void MissingSettingIsAppendedWithItsHelpUnderOneHeader()
    {
        string existing = "matchzy_allow_pause false\nmatchzy_hostname_format \"x\"\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Equal(1, result.AppendedCount);
        Assert.NotNull(result.NewContent);
        Assert.StartsWith("matchzy_allow_pause false\nmatchzy_hostname_format \"x\"\n", result.NewContent);
        Assert.Contains(ConfigCfgMerge.UpdateHeader + "\n// Chat prefix\nmatchzy_chat_prefix [MatchZy]\n", result.NewContent);
        Assert.Single(Occurrences(result.NewContent!, ConfigCfgMerge.UpdateHeader));
    }

    [Fact]
    public void AdminValuesAreNeverChanged()
    {
        string existing = "matchzy_allow_pause false\nmatchzy_chat_prefix [MyServer]\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Contains("matchzy_allow_pause false\n", result.NewContent);
        Assert.Contains("matchzy_chat_prefix [MyServer]\n", result.NewContent);
        Assert.DoesNotContain("matchzy_allow_pause true", result.NewContent);
        Assert.DoesNotContain("matchzy_chat_prefix [MatchZy]", result.NewContent);
    }

    [Fact]
    public void CommentedOutSettingCountsAsPresent()
    {
        string existing = "matchzy_allow_pause true\n// matchzy_chat_prefix [Off]\nmatchzy_hostname_format \"\"\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Null(result.NewContent);
    }

    [Fact]
    public void SettingOnlyMentionedInProseIsStillAppended()
    {
        // "matchzy_hostname_format" appears in a comment, but not as the first token.
        string existing = "matchzy_allow_pause true\nmatchzy_chat_prefix x\n// Example matchzy_hostname_format \"{TEAM1}\"\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Equal(1, result.AppendedCount);
        Assert.Contains("\nmatchzy_hostname_format \"\"\n", result.NewContent);
    }

    [Fact]
    public void SecondMergeAddsNothing()
    {
        var first = ConfigCfgMerge.Merge("matchzy_allow_pause false\n", Template, AllRegistered);
        var second = ConfigCfgMerge.Merge(first.NewContent!, Template, AllRegistered);

        Assert.Null(second.NewContent);
    }

    [Fact]
    public void LaterUpdateReusesTheExistingHeader()
    {
        string existing = "matchzy_allow_pause true\n\n" + ConfigCfgMerge.UpdateHeader + "\nmatchzy_chat_prefix x\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Equal(1, result.AppendedCount);
        Assert.Single(Occurrences(result.NewContent!, ConfigCfgMerge.UpdateHeader));
    }

    [Fact]
    public void DuplicateUpdateHeadersAreCollapsed()
    {
        string existing = Template + "\n" + ConfigCfgMerge.UpdateHeader + "\n// note\n" + ConfigCfgMerge.UpdateHeader + "\n" + ConfigCfgMerge.UpdateHeader + "\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.True(result.CollapsedDuplicateHeaders);
        Assert.Single(Occurrences(result.NewContent!, ConfigCfgMerge.UpdateHeader));
        Assert.Contains("// note\n", result.NewContent);
    }

    [Fact]
    public void RetiredSettingIsRemovedButItsCommentsStay()
    {
        string existing = Template + "\n// old option\nmatchzy_random_spawns 1\n// matchzy_ready_hide_warmup_hud 1\n";

        var result = ConfigCfgMerge.Merge(existing, Template, registeredNames: null);

        Assert.Equal(new[] { "matchzy_random_spawns" }, result.RemovedNames);
        Assert.DoesNotContain("matchzy_random_spawns 1", result.NewContent);
        Assert.Contains("// old option\n", result.NewContent);
        Assert.Contains("// matchzy_ready_hide_warmup_hud 1\n", result.NewContent);
        Assert.False(result.PruneSkipped);
    }

    [Fact]
    public void UnregisteredSettingIsPrunedWhenTheScanIsComplete()
    {
        string existing = Template + "matchzy_gone_option 5\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Equal(new[] { "matchzy_gone_option" }, result.RemovedNames);
        Assert.DoesNotContain("matchzy_gone_option", result.NewContent);
    }

    [Fact]
    public void NothingIsPrunedWhenTheScanIsIncomplete()
    {
        // The scan missed matchzy_hostname_format, a template setting: it cannot be trusted.
        var partial = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "matchzy_allow_pause", "matchzy_chat_prefix" };
        string existing = Template + "matchzy_gone_option 5\n";

        var result = ConfigCfgMerge.Merge(existing, Template, partial);

        Assert.True(result.PruneSkipped);
        Assert.Null(result.NewContent);
    }

    [Fact]
    public void RegisteredSettingOutsideTheTemplateIsKept()
    {
        // Convar-only settings (debug flags, secrets) are registered but not in config.cfg.
        var registered = new HashSet<string>(AllRegistered, StringComparer.OrdinalIgnoreCase) { "matchzy_coach_debug" };
        string existing = Template + "matchzy_coach_debug 1\n";

        var result = ConfigCfgMerge.Merge(existing, Template, registered);

        Assert.Null(result.NewContent);
    }

    [Fact]
    public void NonMatchZyLinesAreNeverPruned()
    {
        string existing = Template + "sv_cheats 0\nexec something.cfg\n";

        var result = ConfigCfgMerge.Merge(existing, Template, AllRegistered);

        Assert.Null(result.NewContent);
    }

    [Fact]
    public void WindowsLineEndingsAreHandled()
    {
        string existing = "matchzy_allow_pause false\r\nmatchzy_chat_prefix x\r\n";

        var result = ConfigCfgMerge.Merge(existing, Template.Replace("\n", "\r\n"), AllRegistered);

        Assert.Equal(1, result.AppendedCount);
        Assert.Contains("matchzy_allow_pause false\n", result.NewContent);
        Assert.Contains("matchzy_hostname_format \"\"", result.NewContent);
    }

    [Fact]
    public void ShippedTemplateMergesIntoItselfWithoutChanges()
    {
        string template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cfg", "config.cfg"));

        var result = ConfigCfgMerge.Merge(template, template, registeredNames: null);

        Assert.Null(result.NewContent);
    }

    [Fact]
    public void ShippedTemplateHasNoRetiredSettings()
    {
        string template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cfg", "config.cfg"));

        foreach (string retired in ConfigCfgMerge.RetiredCvars)
            Assert.DoesNotMatch($@"(?m)^\s*(//\s*)?{retired}\b", template);
    }

    private static IEnumerable<int> Occurrences(string text, string value)
    {
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            yield return i;
    }
}
