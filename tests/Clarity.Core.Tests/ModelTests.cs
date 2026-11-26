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
}
