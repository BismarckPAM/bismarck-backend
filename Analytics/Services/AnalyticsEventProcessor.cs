using System.Text.Json;
using Analytics.Service.Models;
using Messaging;

namespace Analytics.Service.Services;

/// <summary>
/// Turns a raw <see cref="SecurityEvent{TMetadata}"/> envelope into the
/// normalized <see cref="AnalyticsEvent"/> row the database stores.
///
/// This is intentionally a pure, side-effect-free component: the Kafka consumer
/// stays thin (subscribe / deserialize / persist / commit) so the normalization
/// rules that actually define the BIS-402 numbers can be unit tested directly,
/// without a broker or a database.
/// </summary>
public static class AnalyticsEventProcessor
{
    /// <summary>
    /// Builds the analytics row for a successfully deserialized event.
    /// <paramref name="consumedAt"/> is injected so the value is deterministic
    /// in tests.
    /// </summary>
    public static AnalyticsEvent Create(
        SecurityEvent<JsonElement> securityEvent,
        string sourceTopic,
        DateTimeOffset consumedAt)
    {
        var occurredAtUtc = securityEvent.OccurredAt.ToUniversalTime();
        var metadata = securityEvent.Metadata;

        return new AnalyticsEvent
        {
            Id = Guid.NewGuid(),
            EventId = securityEvent.EventId,
            SourceTopic = sourceTopic,
            EventType = securityEvent.EventType,
            OccurredAt = occurredAtUtc,
            OccurredAtDate = DateOnly.FromDateTime(occurredAtUtc.UtcDateTime),
            Actor = securityEvent.Actor,
            Resource = NullIfBlank(securityEvent.Resource),
            ResourceName = ResolveResourceName(securityEvent, metadata),
            Action = securityEvent.Action,
            Outcome = securityEvent.Outcome,
            DenialReason = ResolveDenialReason(securityEvent, metadata),
            Metadata = SerializeMetadata(metadata),
            ConsumedAt = consumedAt
        };
    }

    /// <summary>
    /// Resolves the friendly resource label.
    /// Metadata.ResourceName wins when the producer supplied one; otherwise the
    /// SecurityEvent.Resource identifier is used as the fallback. Metadata
    /// variations (camelCase key, non-string value, absent object) are tolerated
    /// rather than throwing, because Kafka consumers must never die on an
    /// unexpected payload shape.
    /// </summary>
    public static string? ResolveResourceName(
        SecurityEvent<JsonElement> securityEvent,
        JsonElement metadata)
    {
        var fromMetadata = ReadMetadataString(
            metadata,
            AnalyticsMetricMap.ResourceNameMetadataKey,
            AnalyticsMetricMap.ResourceNameMetadataKeyCamel);

        return NullIfBlank(fromMetadata) ?? NullIfBlank(securityEvent.Resource);
    }

    /// <summary>
    /// Resolves the normalized denial reason.
    /// AccessDenied reads Metadata.Reason, ApprovalRejected reads
    /// Metadata.RejectionReason. Anything blank or genuinely unavailable becomes
    /// UNKNOWN so a missing reason aggregates into one comparable bucket instead
    /// of fragmenting the distribution. Non-denial events carry no reason.
    /// </summary>
    public static string? ResolveDenialReason(
        SecurityEvent<JsonElement> securityEvent,
        JsonElement metadata)
    {
        var key = securityEvent.EventType switch
        {
            AnalyticsMetricMap.AccessDeniedEventType => AnalyticsMetricMap.ReasonMetadataKey,
            AnalyticsMetricMap.ApprovalRejectedEventType => AnalyticsMetricMap.RejectionReasonMetadataKey,
            _ => null
        };

        if (key is null)
        {
            return null;
        }

        var value = ReadMetadataString(metadata, key);

        return NullIfBlank(value) ?? AnalyticsMetricMap.UnknownDenialReason;
    }

    /// <summary>
    /// Reads a string property from the metadata object, case-insensitively.
    /// Returns null for a missing key, a non-object metadata payload, a JSON
    /// null, or a non-string value - never throws.
    /// </summary>
    private static string? ReadMetadataString(JsonElement metadata, params string[] propertyNames)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in propertyNames)
        {
            if (!metadata.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                // A producer that sent a number/object here is still a usable
                // label for grouping, so fall back to its raw text.
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static string SerializeMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "{}";
        }

        try
        {
            var raw = metadata.GetRawText();

            return string.IsNullOrWhiteSpace(raw) ? "{}" : raw;
        }
        catch (InvalidOperationException)
        {
            // Defensive: GetRawText can throw for a disposed/invalid element.
            return "{}";
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
