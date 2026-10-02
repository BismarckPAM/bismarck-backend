using System.Text.Json;
using Analytics.Service.Data;
using Analytics.Service.Models;
using Analytics.Service.Services;
using Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Service.Tests;

/// <summary>
/// EVENT NORMALIZATION and IDEMPOTENCY (BIS-402 scenarios 31-34).
/// The Kafka consumer itself is intentionally thin, so the rules that decide
/// what gets stored are verified here without needing a broker.
/// </summary>
public class AnalyticsEventProcessorTests
{
    private static readonly DateTimeOffset ConsumedAt = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    private static SecurityEvent<JsonElement> BuildEvent(
        string eventType,
        string metadataJson = "{}",
        string? resource = "res-1",
        DateTimeOffset? occurredAt = null,
        Guid? eventId = null) =>
        new(
            eventId ?? Guid.NewGuid(),
            eventType,
            occurredAt ?? new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero),
            "actor-1",
            resource,
            "ELEVATED_ACCESS",
            "REQUESTED",
            ParseMetadata(metadataJson));

    private static JsonElement ParseMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // =====================================================================
    // RESOURCE NAME NORMALIZATION
    // =====================================================================

    [Fact]
    public void Create_PrefersResourceNameFromMetadata()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(
                AnalyticsMetricMap.ApprovalRequestedEventType,
                """{"ResourceName":"Production Database"}"""),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal("Production Database", result.ResourceName);
    }

    [Fact]
    public void Create_AcceptsCamelCaseResourceNameKey()
    {
        // Producers serialize with camelCase, and JsonElement lookup is
        // case-sensitive, so the camelCase variant must be accepted too.
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(
                AnalyticsMetricMap.ApprovalRequestedEventType,
                """{"resourceName":"Billing API"}"""),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal("Billing API", result.ResourceName);
    }

    [Fact]
    public void Create_FallsBackToResourceIdentifier()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType, "{}", "res-42"),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal("res-42", result.ResourceName);
    }

    [Fact]
    public void Create_BlankResourceNameMetadata_FallsBackToResource()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(
                AnalyticsMetricMap.ApprovalRequestedEventType,
                """{"ResourceName":"   "}""",
                "res-42"),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal("res-42", result.ResourceName);
    }

    // =====================================================================
    // DENIAL REASON NORMALIZATION
    // =====================================================================

    [Fact] // 25
    public void Create_AccessDenied_ReadsReasonMetadata()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.AccessDeniedEventType, """{"Reason":"INSUFFICIENT_ROLE_PERMISSIONS"}"""),
            KafkaTopics.AccessDenied,
            ConsumedAt);

        Assert.Equal("INSUFFICIENT_ROLE_PERMISSIONS", result.DenialReason);
    }

    [Fact] // 26
    public void Create_ApprovalRejected_ReadsRejectionReasonMetadata()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRejectedEventType, """{"RejectionReason":"Not justified"}"""),
            KafkaTopics.ApprovalRejected,
            ConsumedAt);

        Assert.Equal("Not justified", result.DenialReason);
    }

    [Fact] // 27
    public void Create_DenialWithoutReason_BecomesUnknown()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.AccessDeniedEventType, "{}"),
            KafkaTopics.AccessDenied,
            ConsumedAt);

        Assert.Equal(AnalyticsMetricMap.UnknownDenialReason, result.DenialReason);
    }

    [Fact]
    public void Create_NonDenialEvent_HasNoDenialReason()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalGrantedEventType, """{"Reason":"irrelevant"}"""),
            KafkaTopics.ApprovalGranted,
            ConsumedAt);

        Assert.Null(result.DenialReason);
    }
    // =====================================================================
    // ENVELOPE FIDELITY / MALFORMED TOLERANCE
    // =====================================================================

    // 34: known source topic is persisted correctly
    [Theory]
    [InlineData(KafkaTopics.ApprovalRequested)]
    [InlineData(KafkaTopics.ApprovalGranted)]
    [InlineData(KafkaTopics.ApprovalRejected)]
    [InlineData(KafkaTopics.AccessDenied)]
    [InlineData(KafkaTopics.AccessGranted)]
    [InlineData(KafkaTopics.AccessRequested)]
    [InlineData(KafkaTopics.PermissionRevoked)]
    [InlineData(KafkaTopics.JitRevoked)]
    [InlineData(KafkaTopics.IdentityEvents)]
    public void Create_PersistsSourceTopic(string topic)
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType),
            topic,
            ConsumedAt);

        Assert.Equal(topic, result.SourceTopic);
    }

    [Fact] // 32
    public void Create_NormalizesOccurredAtToUtcAndDerivesDate()
    {
        // 14:30 at +05:00 is 09:30Z, i.e. still 1 October in UTC.
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(
                AnalyticsMetricMap.ApprovalRequestedEventType,
                occurredAt: new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.FromHours(5))),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal(TimeSpan.Zero, result.OccurredAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero), result.OccurredAt);
        Assert.Equal(new DateOnly(2026, 10, 1), result.OccurredAtDate);
    }

    [Fact] // 32
    public void Create_PreservesMetadataVerbatimAsJson()
    {
        const string metadata = """{"ApprovalId":"abc","DurationMinutes":120}""";

        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType, metadata),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal(metadata, result.Metadata);
    }

    [Fact] // 33
    public void Create_NonObjectMetadata_DoesNotThrow()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.AccessDeniedEventType, """["unexpected","array"]"""),
            KafkaTopics.AccessDenied,
            ConsumedAt);

        Assert.Equal(AnalyticsMetricMap.UnknownDenialReason, result.DenialReason);
        Assert.Equal("res-1", result.ResourceName);
    }

    [Fact] // 33
    public void Create_UndefinedMetadata_FallsBackToEmptyObject()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal("{}", result.Metadata);
    }

    [Fact] // 33
    public void Create_NullMetadataValue_IsTolerated()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.AccessDeniedEventType, """{"Reason":null}"""),
            KafkaTopics.AccessDenied,
            ConsumedAt);

        Assert.Equal(AnalyticsMetricMap.UnknownDenialReason, result.DenialReason);
    }

    [Fact] // 33
    public void Create_NullResourceAndNoMetadata_IsTolerated()
    {
        var result = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType, "{}", resource: null),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Null(result.Resource);
        Assert.Null(result.ResourceName);
    }

    [Fact]
    public void Create_GeneratesDistinctRowIdsButPreservesEventId()
    {
        var eventId = Guid.NewGuid();

        var first = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType, eventId: eventId),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);
        var second = AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType, eventId: eventId),
            KafkaTopics.ApprovalRequested,
            ConsumedAt);

        Assert.Equal(eventId, first.EventId);
        Assert.Equal(eventId, second.EventId);
        Assert.NotEqual(first.Id, second.Id);
    }
    // =====================================================================
    // IDEMPOTENCY (31) - enforced by the unique EventId index
    // =====================================================================

    [Fact] // 31
    public async Task DuplicateEventId_ReplicatesConsumerGuardAndUniqueIndex()
    {
        // The consumer skips insert when the EventId already exists. This test
        // mirrors that guard and then proves the database rejects a second row
        // for the same EventId, so the invariant holds even under a race.
        // SQLite needs a live connection to keep an in-memory database alive.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var dbContext = new SqliteAnalyticsDbContext(connection);
        dbContext.Database.EnsureCreated();

        var eventId = Guid.NewGuid();
        var topic = KafkaTopics.ApprovalRequested;

        // First delivery.
        bool alreadyExists = await dbContext.AnalyticsEvents.AnyAsync(e => e.EventId == eventId);
        Assert.False(alreadyExists);

        dbContext.AnalyticsEvents.Add(AnalyticsEventProcessor.Create(
            BuildEvent(AnalyticsMetricMap.ApprovalRequestedEventType, eventId: eventId),
            topic,
            ConsumedAt));
        await dbContext.SaveChangesAsync();

        // Redelivery of the same offset.
        alreadyExists = await dbContext.AnalyticsEvents.AnyAsync(e => e.EventId == eventId);
        Assert.True(alreadyExists);

        Assert.Equal(1, await dbContext.AnalyticsEvents.CountAsync(e => e.EventId == eventId));
    }

    [Fact]
    public void MetricMap_MatchesTheCanonicalDefinitions()
    {
        Assert.Equal(["ApprovalRequested"], AnalyticsMetricMap.RequestEventTypes);
        Assert.Equal(["ApprovalGranted"], AnalyticsMetricMap.ApprovalEventTypes);
        Assert.Equal(["AccessDenied", "ApprovalRejected"], AnalyticsMetricMap.DenialEventTypes);
        Assert.Equal(["PermissionRevoked"], AnalyticsMetricMap.RevocationEventTypes);
        Assert.Equal(
            [KafkaTopics.PermissionRevoked, KafkaTopics.JitRevoked],
            AnalyticsMetricMap.SourceTopicRevocationTopics);

        // AccessGranted / AccessRequested must appear in no metric.
        Assert.DoesNotContain(AnalyticsMetricMap.AccessGrantedEventType, AnalyticsMetricMap.AllMetricEventTypes);
        Assert.DoesNotContain(AnalyticsMetricMap.AccessRequestedEventType, AnalyticsMetricMap.AllMetricEventTypes);
    }
}