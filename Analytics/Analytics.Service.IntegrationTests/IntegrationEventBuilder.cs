using System.Text.Json;
using Analytics.Service.Models;
using Analytics.Service.Services;
using Messaging;

namespace Analytics.Service.IntegrationTests;

/// <summary>
/// Builds real <see cref="SecurityEvent{TMetadata}"/> envelopes for the
/// integration tests and runs them through the production normalizer, so the
/// stored rows are byte-for-byte what the Kafka consumer would have written.
/// </summary>
internal static class IntegrationEventBuilder
{
    public static AnalyticsEvent Create(
        string eventType,
        DateTimeOffset occurredAt,
        string sourceTopic,
        object? metadata = null,
        string? resource = "res-1",
        Guid? eventId = null)
    {
        var securityEvent = new SecurityEvent<JsonElement>(
            eventId ?? Guid.NewGuid(),
            eventType,
            occurredAt,
            "integration-user",
            resource,
            "ELEVATED_ACCESS",
            "REQUESTED",
            SerializeMetadata(metadata));

        return AnalyticsEventProcessor.Create(securityEvent, sourceTopic, DateTimeOffset.UtcNow);
    }

    public static AnalyticsEvent ApprovalRequested(
        DateTimeOffset occurredAt,
        string? resourceName = "Production Database",
        string? resource = "res-1",
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.ApprovalRequestedEventType,
            occurredAt,
            KafkaTopics.ApprovalRequested,
            resourceName is null ? null : new { ResourceName = resourceName },
            resource,
            eventId);

    public static AnalyticsEvent ApprovalGranted(DateTimeOffset occurredAt, Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.ApprovalGrantedEventType,
            occurredAt,
            KafkaTopics.ApprovalGranted,
            new { ResourceName = "Production Database" },
            eventId: eventId);

    public static AnalyticsEvent ApprovalRejected(
        DateTimeOffset occurredAt,
        string? rejectionReason,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.ApprovalRejectedEventType,
            occurredAt,
            KafkaTopics.ApprovalRejected,
            rejectionReason is null ? null : new { RejectionReason = rejectionReason },
            eventId: eventId);

    public static AnalyticsEvent AccessDenied(
        DateTimeOffset occurredAt,
        string? reason,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.AccessDeniedEventType,
            occurredAt,
            KafkaTopics.AccessDenied,
            reason is null ? null : new { Reason = reason },
            eventId: eventId);

    public static AnalyticsEvent PermissionRevoked(
        DateTimeOffset occurredAt,
        string sourceTopic = KafkaTopics.PermissionRevoked,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.PermissionRevokedEventType,
            occurredAt,
            sourceTopic,
            new { Reason = "Automatic expiration by background worker past TTL." },
            eventId: eventId);

    public static AnalyticsEvent AccessGranted(DateTimeOffset occurredAt, Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.AccessGrantedEventType,
            occurredAt,
            KafkaTopics.AccessGranted,
            eventId: eventId);

    public static AnalyticsEvent AccessRequested(DateTimeOffset occurredAt, Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.AccessRequestedEventType,
            occurredAt,
            KafkaTopics.AccessRequested,
            new { Reason = "ELEVATED_ACCESS_REQUIRES_APPROVAL" },
            eventId: eventId);

    private static JsonElement SerializeMetadata(object? metadata)
    {
        if (metadata is null)
        {
            return default;
        }

        var json = JsonSerializer.Serialize(metadata);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
