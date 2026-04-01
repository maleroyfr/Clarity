using Clarity.Collectors.Contracts;
using Clarity.Collectors.Graph.Auth;
using Clarity.Collectors.Graph.Entra;
using Clarity.Domain.Environments;
using Clarity.SharedContracts.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Clarity.Collectors.Graph.Tests;

public sealed class EntraUsersCollectorTests
{
    private static CollectorRunContext BuildContext(
        ILogger? logger = null,
        CollectorOptions? options = null)
    {
        var authConfig = AuthConfiguration.CreateClientSecret(
            Guid.NewGuid(),
            WorkloadArea.EntraId,
            clientId: "test-client-id",
            tenantId: "test-tenant-id",
            secretReference: "TEST_SECRET_REF");

        return new CollectorRunContext(
            SnapshotId: Guid.NewGuid(),
            CollectorRunId: Guid.NewGuid(),
            EnvironmentId: Guid.NewGuid(),
            WorkloadArea: WorkloadArea.EntraId,
            AuthConfig: authConfig,
            Options: options ?? new CollectorOptions(),
            Logger: logger ?? NullLogger.Instance,
            CancellationToken: CancellationToken.None);
    }

    private static List<User> BuildUsers(int count) =>
        Enumerable.Range(1, count).Select(i => new User
        {
            Id = $"user-id-{i}",
            DisplayName = $"User {i}",
            UserPrincipalName = $"user{i}@contoso.com",
            Mail = $"user{i}@contoso.com",
            AccountEnabled = true,
            UserType = "Member",
            JobTitle = $"Engineer {i}",
            Department = "Engineering",
            OfficeLocation = "Seattle",
            UsageLocation = "US",
            CreatedDateTime = DateTimeOffset.UtcNow.AddDays(-i),
            AssignedLicenses = []
        }).ToList();

    [Fact]
    public async Task HappyPath_ReturnsUsers()
    {
        // Arrange
        var factory = Substitute.For<IGraphClientFactory>();
        var fetcher = Substitute.For<IGraphUserFetcher>();
        var fakeUsers = BuildUsers(3);

        factory.Create(Arg.Any<AuthConfiguration>()).Returns((GraphServiceClient)null!);
        fetcher.FetchAllUsersAsync(
                Arg.Any<GraphServiceClient>(),
                Arg.Any<CollectorOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(fakeUsers);

        var collector = new EntraUsersCollector(factory, fetcher);
        var context = BuildContext();

        // Act
        var result = await collector.RunAsync(context);

        // Assert
        result.Status.Should().Be(CollectorRunStatus.Completed);
        result.ItemsCollected.Should().Be(3);
        result.Objects.Should().HaveCount(3);
        result.Errors.Should().BeEmpty();

        var first = result.Objects[0];
        first.ExternalId.Should().Be("user-id-1");
        first.DisplayName.Should().Be("User 1");
        first.ObjectType.Should().Be(InventoryObjectType.EntraUser);
        first.GetProperty("userPrincipalName").Should().Be("user1@contoso.com");
        first.GetProperty("accountEnabled").Should().Be("True");
        first.GetProperty("department").Should().Be("Engineering");
    }

    [Fact]
    public async Task EmptyTenant_ReturnsEmptySuccess()
    {
        // Arrange
        var factory = Substitute.For<IGraphClientFactory>();
        var fetcher = Substitute.For<IGraphUserFetcher>();

        factory.Create(Arg.Any<AuthConfiguration>()).Returns((GraphServiceClient)null!);
        fetcher.FetchAllUsersAsync(
                Arg.Any<GraphServiceClient>(),
                Arg.Any<CollectorOptions>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var collector = new EntraUsersCollector(factory, fetcher);
        var context = BuildContext();

        // Act
        var result = await collector.RunAsync(context);

        // Assert
        result.Status.Should().Be(CollectorRunStatus.Completed);
        result.ItemsCollected.Should().Be(0);
        result.Objects.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task GraphError_ReturnsFailure()
    {
        // Arrange
        var factory = Substitute.For<IGraphClientFactory>();
        var fetcher = Substitute.For<IGraphUserFetcher>();

        factory.Create(Arg.Any<AuthConfiguration>()).Returns((GraphServiceClient)null!);

        var odataError = new ODataError { ResponseStatusCode = 503 };
        fetcher.FetchAllUsersAsync(
                Arg.Any<GraphServiceClient>(),
                Arg.Any<CollectorOptions>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(odataError);

        var collector = new EntraUsersCollector(factory, fetcher);

        // Use MaxRetries = 0 so the test completes quickly without backoff waits
        var context = BuildContext(options: new CollectorOptions(MaxRetries: 0));

        // Act
        var result = await collector.RunAsync(context);

        // Assert
        result.Status.Should().Be(CollectorRunStatus.Failed);
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Code.Should().Be("GRAPH_ODATA_ERROR");
        result.ItemsCollected.Should().Be(0);
    }
}
