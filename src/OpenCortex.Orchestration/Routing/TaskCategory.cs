namespace OpenCortex.Orchestration.Routing;

/// <summary>
/// Categories of tasks for routing decisions.
/// </summary>
public enum TaskCategory
{
    /// <summary>
    /// General conversation or uncategorized task.
    /// </summary>
    General,

    /// <summary>
    /// Code generation, editing, or debugging.
    /// </summary>
    Code,

    /// <summary>
    /// Planning, architecture, or system design.
    /// </summary>
    Planning,

    /// <summary>
    /// Writing, documentation, or long-form content.
    /// </summary>
    Writing,

    /// <summary>
    /// Analysis, comparison, or evaluation.
    /// </summary>
    Analysis,

    /// <summary>
    /// Quick lookup or simple question.
    /// </summary>
    Quick,

    /// <summary>
    /// Private or sensitive content requiring local processing.
    /// </summary>
    Private,

    /// <summary>
    /// Math, calculation, or reasoning task.
    /// </summary>
    Reasoning
}

/// <summary>
/// Result of task classification.
/// </summary>
public sealed record TaskClassification
{
    /// <summary>
    /// Primary category of the task.
    /// </summary>
    public required TaskCategory Category { get; init; }

    /// <summary>
    /// Confidence score (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>
    /// Secondary categories that also apply.
    /// </summary>
    public IReadOnlyList<TaskCategory>? SecondaryCategories { get; init; }

    /// <summary>
    /// Detected keywords that influenced classification.
    /// </summary>
    public IReadOnlyList<string>? DetectedKeywords { get; init; }

    /// <summary>
    /// Whether this task should be marked as high-stakes (multi-model).
    /// </summary>
    public bool IsHighStakes { get; init; }
}
