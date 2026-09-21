using System.Text.Json.Serialization;

namespace Umbraco.Community.SchemeWeaver.TypeSafe.Client;

/// <summary>
/// One System One question. The three primitives share a wire shape — <c>type</c>,
/// <c>instructions</c>, <c>criteria</c> — and differ only in what <c>criteria</c> holds:
/// a map of option key to description for a Choice, an ordered list of level descriptions
/// for a Score, and an optional <c>{ "true": …, "false": … }</c> object for a Noul (a plain
/// string there is rejected with HTTP 422). Use the factories rather than the constructor.
/// </summary>
public sealed class SystemOneQuestion
{
    /// <summary>The question id is the dictionary key in the request and is never sent to the model.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>The judgment being asked for. A string, or a structured object/array when that clarifies it.</summary>
    [JsonPropertyName("instructions")]
    public object Instructions { get; init; } = string.Empty;

    /// <summary>The possible answers; shape depends on <see cref="Type"/>. Omitted from the wire when null.</summary>
    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Criteria { get; init; }

    /// <summary>Picks one option out of a defined set (at most 255 options).</summary>
    public static SystemOneQuestion Choice(object instructions, IReadOnlyDictionary<string, string> criteria)
        => new() { Type = "choice", Instructions = instructions, Criteria = criteria };

    /// <summary>Probability that a condition holds. Criteria, when given, describes what true and false mean.</summary>
    public static SystemOneQuestion Noul(object instructions, NoulCriteria? criteria = null)
        => new() { Type = "noul", Instructions = instructions, Criteria = criteria };

    /// <summary>A position on 2–10 ordered levels, each described concretely.</summary>
    public static SystemOneQuestion Score(object instructions, IReadOnlyList<string> levels)
        => new() { Type = "score", Instructions = instructions, Criteria = levels };
}

/// <summary>What a yes and a no mean for a Noul question.</summary>
public sealed record NoulCriteria(
    [property: JsonPropertyName("true")] string True,
    [property: JsonPropertyName("false")] string False);

/// <summary>The request body for <c>POST /v1/systemone</c>.</summary>
public sealed class SystemOneRequest
{
    /// <summary>Shared context every question in the request can see. String, object or array.</summary>
    [JsonPropertyName("state")]
    public object State { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = "jev-latest";

    [JsonPropertyName("questions")]
    public IReadOnlyDictionary<string, SystemOneQuestion> Questions { get; init; }
        = new Dictionary<string, SystemOneQuestion>();
}

/// <summary>
/// One answer. Which members are populated depends on <see cref="Type"/>: a Choice carries
/// <see cref="Choice"/>, <see cref="Probabilities"/> and <see cref="Confidence"/>; a Noul
/// carries <see cref="Noul"/> only (its probability is its certainty); a Score carries
/// <see cref="Score"/>, <see cref="Legend"/>, <see cref="Probabilities"/> and <see cref="Confidence"/>.
/// </summary>
public sealed class SystemOneAnswer
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>The selected option key (Choice).</summary>
    [JsonPropertyName("choice")]
    public string? Choice { get; init; }

    /// <summary>Probability per option (Choice) or per level (Score).</summary>
    [JsonPropertyName("probabilities")]
    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    /// <summary>How concentrated the distribution is on the selected answer, 0–1 (Choice, Score).</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    /// <summary>Probability that the condition holds, 0–1 (Noul).</summary>
    [JsonPropertyName("noul")]
    public double? Noul { get; init; }

    /// <summary>Probability-weighted position on the levels (Score).</summary>
    [JsonPropertyName("score")]
    public double? Score { get; init; }

    /// <summary>Level index to description (Score).</summary>
    [JsonPropertyName("legend")]
    public IReadOnlyDictionary<string, string>? Legend { get; init; }
}

/// <summary>Token accounting for one request. Output tokens are free on Jev; input is billed.</summary>
public sealed class SystemOneUsage
{
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; init; }
}

/// <summary>The response body for <c>POST /v1/systemone</c>.</summary>
public sealed class SystemOneResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("answers")]
    public IReadOnlyDictionary<string, SystemOneAnswer> Answers { get; init; }
        = new Dictionary<string, SystemOneAnswer>();

    [JsonPropertyName("usage")]
    public SystemOneUsage? Usage { get; init; }
}
