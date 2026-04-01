using Clarity.Domain.Common;
using Clarity.Domain.Environments;
using Clarity.Domain.Snapshots;
using Clarity.SharedContracts.Enums;
using FluentAssertions;

namespace Clarity.Domain.Tests.Snapshots;

public sealed class SnapshotTests
{
    private static readonly Guid _customerId = Guid.NewGuid();
    private static readonly Guid _envId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldInitializeWithDraftStatus()
    {
        var snapshot = Snapshot.Create(_customerId, _envId, "Snapshot 1",
            [WorkloadArea.EntraId, WorkloadArea.Intune]);

        snapshot.Status.Should().Be(SnapshotStatus.Draft);
        snapshot.IsImmutable.Should().BeFalse();
        snapshot.WorkloadScope.Should().BeEquivalentTo(new[] { WorkloadArea.EntraId, WorkloadArea.Intune });
    }

    [Fact]
    public void StartCollectorRun_ShouldChangeStatusToRunning()
    {
        var snapshot = Snapshot.Create(_customerId, _envId, "S1", [WorkloadArea.EntraId]);

        snapshot.StartCollectorRun(WorkloadArea.EntraId, CollectorType.Graph, "1.0.0");

        snapshot.Status.Should().Be(SnapshotStatus.Running);
        snapshot.CollectorRuns.Should().HaveCount(1);
    }

    [Fact]
    public void Seal_AllSuccess_ShouldSetCompleted()
    {
        var snapshot = Snapshot.Create(_customerId, _envId, "S1", [WorkloadArea.EntraId]);
        var run = snapshot.StartCollectorRun(WorkloadArea.EntraId, CollectorType.Graph, "1.0.0");
        run.Complete(100, ["User.Read.All"]);

        snapshot.Seal();

        snapshot.Status.Should().Be(SnapshotStatus.Completed);
        snapshot.IsImmutable.Should().BeTrue();
        snapshot.FinalizedAt.Should().NotBeNull();
    }

    [Fact]
    public void Seal_WithFailedRun_ShouldSetPartial()
    {
        var snapshot = Snapshot.Create(_customerId, _envId, "S1",
            [WorkloadArea.EntraId, WorkloadArea.Intune]);

        var run1 = snapshot.StartCollectorRun(WorkloadArea.EntraId, CollectorType.Graph, "1.0.0");
        run1.Complete(50, []);

        var run2 = snapshot.StartCollectorRun(WorkloadArea.Intune, CollectorType.Graph, "1.0.0");
        run2.Fail(new CollectorError("ERR", "Failed"));

        snapshot.Seal();

        snapshot.Status.Should().Be(SnapshotStatus.Partial);
    }

    [Fact]
    public void StartCollectorRun_AfterSealing_ShouldThrow()
    {
        var snapshot = Snapshot.Create(_customerId, _envId, "S1", [WorkloadArea.EntraId]);
        snapshot.StartCollectorRun(WorkloadArea.EntraId, CollectorType.Graph, "1.0.0").Complete(0, []);
        snapshot.Seal();

        var act = () => snapshot.StartCollectorRun(WorkloadArea.Intune, CollectorType.Graph, "1.0.0");
        act.Should().Throw<DomainException>().WithMessage("*immutable*");
    }
}
