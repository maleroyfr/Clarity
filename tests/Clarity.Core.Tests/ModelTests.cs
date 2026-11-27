using Shouldly;

namespace Clarity.Core.Tests;

public class ModelTests
{
    [Fact]
    public void Archiving_is_idempotent_and_preserves_project_identity()
    {
        var project = new Project { WorkspaceId = Guid.NewGuid(), Name = "Synthetic audit" };
        var id = project.Id;
        project.Archive();
        project.Archive();
        project.Status.ShouldBe(ProjectStatus.Archived);
        project.Id.ShouldBe(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Project_requires_a_name_and_client(string name)
    {
        Should.Throw<ArgumentException>(() => ModelRules.Validate(new Project { Name = name, WorkspaceId = Guid.NewGuid() }));
        Should.Throw<ArgumentException>(() => ModelRules.Validate(new Project { Name = "Synthetic" }));
    }

    [Fact]
    public void Planned_environment_must_be_a_target()
    {
        Should.Throw<ArgumentException>(() => ModelRules.Validate(new ProjectEnvironment
        {
            WorkspaceId = Guid.NewGuid(),
            Role = EnvironmentRole.Source,
            TargetState = TargetState.Planned,
            ExternalId = ""
        }));
        ModelRules.Validate(new ProjectEnvironment
        {
            WorkspaceId = Guid.NewGuid(),
            Role = EnvironmentRole.Target,
            TargetState = TargetState.Planned,
            ExternalId = "",
            Hypotheses = "target.example.invalid"
        });
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://example.invalid\")", true)]
    [InlineData("+33 1 23", true)]
    [InlineData("-2+3", true)]
    [InlineData("@SUM(A1)", true)]
    [InlineData("\tcmd", true)]
    [InlineData("\uFF1D1+1", true)]
    [InlineData("alice@example.test", false)]
    [InlineData("Vague 1", false)]
    public void SpreadsheetTextNeutralizesFormulaLikeValues(string value, bool formula)
    {
        SpreadsheetText.IsFormulaLike(value).ShouldBe(formula);
        SpreadsheetText.Neutralize(value).ShouldBe(formula ? "'" + value : value);
    }

    [Fact]
    public void CsvFieldsAreNeutralizedQuotedAndSemicolonSeparated()
    {
        SpreadsheetText.CsvLine(["alice@example.test", null, "=1+1", "a;b", "say \"hi\"", " padded", "line\nbreak"])
            .ShouldBe("alice@example.test;;\"'=1+1\";\"a;b\";\"say \"\"hi\"\"\";\" padded\";\"line\nbreak\"\r\n");
    }

    [Fact]
    public void ExportFiltersAreNormalizedAndValidated()
    {
        new ExportFilter(EnvironmentRole.Source, Guid.Empty, "  user ", " Enabled ", ExportDecisionFilter.Include, "  Vague 1 ", Guid.Empty).Normalize()
            .ShouldBe(new ExportFilter(EnvironmentRole.Source, null, "user", "Enabled", ExportDecisionFilter.Include, "Vague 1", null));
        new ExportFilter(EnvironmentRole.Target, Status: "Unknown").Normalize().Status.ShouldBe("Unknown");
        Should.Throw<ArgumentException>(() => new ExportFilter(EnvironmentRole.Source, Status: "Missing").Normalize());
        Should.Throw<ArgumentException>(() => new ExportFilter((EnvironmentRole)9).Normalize());
        Should.Throw<ArgumentException>(() => new ExportFilter(EnvironmentRole.Source, Decision: (ExportDecisionFilter)9).Normalize());
        Should.Throw<ArgumentException>(() => new ExportFilter(EnvironmentRole.Source, Type: new string('t', 101)).Normalize());
    }
}
