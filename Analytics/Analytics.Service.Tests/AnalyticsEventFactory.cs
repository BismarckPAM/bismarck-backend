using System.Text.Json;
using Analytics.Service.Models;
using Analytics.Service.Services;
using Messaging;

namespace Analytics.Service.Tests;

/// <summary>
/// Helpers for building realistic AnalyticsEvent rows in unit tests.
/// The event shapes mirror what the Approval and Authorization services emit
/// today, so the tests assert against the real production contract.
/// </summary>
internal static class AnalyticsEventFactory
{
    public const string ResourceId = "res-1111";
    public const string ResourceName = "Production Database";

    /// <summary>Builds a row by running the real production normalizer over a JSON metadata payload.</summary>
    public static AnalyticsEvent Create(
        string eventType,
        DateTimeOffset occurredAt,
        string sourceTopic,
        string? metadataJson = null,
        string? resource = ResourceId,
        string actor = "user-1",
        string action = "ELEVATED_ACCESS",
        string outcome = "REQUESTED",
        Guid? eventId = null)
    {
        var metadata = ParseMetadata(metadataJson);
        var securityEvent = new SecurityEvent<JsonElement>(
            eventId ?? Guid.NewGuid(),
            eventType,
            occurredAt,
            actor,
            resource,
            action,
            outcome,
            metadata);

        return AnalyticsEventProcessor.Create(securityEvent, sourceTopic, DateTimeOffset.UtcNow);
    }

    public static AnalyticsEvent ApprovalRequested(
        DateTimeOffset occurredAt,
        string? resourceName = ResourceName,
        string? resource = ResourceId,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.ApprovalRequestedEventType,
            occurredAt,
            KafkaTopics.ApprovalRequested,
            resourceName is null
                ? "{}"
                : JsonSerializer.Serialize(new { ResourceName = resourceName }),
            resource,
            outcome: "REQUESTED",
            eventId: eventId);

    public static AnalyticsEvent ApprovalGranted(DateTimeOffset occurredAt, Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.ApprovalGrantedEventType,
            occurredAt,
            KafkaTopics.ApprovalGranted,
            "{}",
            outcome: "APPROVED",
            eventId: eventId);

    public static AnalyticsEvent ApprovalRejected(
        DateTimeOffset occurredAt,
        string? rejectionReason,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.ApprovalRejectedEventType,
            occurredAt,
            KafkaTopics.ApprovalRejected,
            rejectionReason is null
                ? "{}"
                : JsonSerializer.Serialize(new { RejectionReason = rejectionReason }),
            outcome: "REJECTED",
            eventId: eventId);

    public static AnalyticsEvent AccessDenied(
        DateTimeOffset occurredAt,
        string? reason,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.AccessDeniedEventType,
            occurredAt,
            KafkaTopics.AccessDenied,
            reason is null ? "{}" : JsonSerializer.Serialize(new { Reason = reason }),
            outcome: "DENIED",
            eventId: eventId);

    public static AnalyticsEvent PermissionRevoked(
        DateTimeOffset occurredAt,
        string sourceTopic = KafkaTopics.PermissionRevoked,
        Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.PermissionRevokedEventType,
            occurredAt,
            sourceTopic,
            JsonSerializer.Serialize(new { Reason = "Automatic expiration by background worker past TTL." }),
            action: "EXPIRE_PERMISSION",
            outcome: "SUCCESS",
            eventId: eventId);

    public static AnalyticsEvent AccessGranted(DateTimeOffset occurredAt, Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.AccessGrantedEventType,
            occurredAt,
            KafkaTopics.AccessGranted,
            "{}",
            action: "READ",
            outcome: "ALLOWED",
            eventId: eventId);

    public static AnalyticsEvent AccessRequested(DateTimeOffset occurredAt, Guid? eventId = null) =>
        Create(
            AnalyticsMetricMap.AccessRequestedEventType,
            occurredAt,
            KafkaTopics.AccessRequested,
            JsonSerializer.Serialize(new { Reason = "ELEVATED_ACCESS_REQUIRES_APPROVAL" }),
            action: "READ",
            outcome: "APPROVAL_REQUIRED",
            eventId: eventId);

    private static JsonElement ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return default;
        }

        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.Clone();
    }
}
