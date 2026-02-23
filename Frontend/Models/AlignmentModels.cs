using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Frontend.Models;

public record AlignmentConstraint(
    [property: JsonPropertyName("time")] double Time,
    [property: JsonPropertyName("phoneme_index")] int PhonemeIndex,
    [property: JsonPropertyName("type")] string Type = "anchor"
);

public record AlignmentInterval(
    [property: JsonPropertyName("start")] double Start,
    [property: JsonPropertyName("end")] double End,
    [property: JsonPropertyName("text")] string Text
);

public record AlignResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("intervals")] List<AlignmentInterval> Intervals,
    [property: JsonPropertyName("constraints")] List<AlignmentConstraint>? Constraints = null
);
