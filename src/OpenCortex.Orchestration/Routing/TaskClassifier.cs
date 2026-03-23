using System.Text.RegularExpressions;

namespace OpenCortex.Orchestration.Routing;

/// <summary>
/// Classifies user messages into task categories for routing.
/// </summary>
public interface ITaskClassifier
{
    /// <summary>
    /// Classify a user message into a task category.
    /// </summary>
    TaskClassification Classify(string message);
}

/// <summary>
/// Rule-based task classifier using keyword matching.
/// </summary>
public sealed partial class KeywordTaskClassifier : ITaskClassifier
{
    private static readonly Dictionary<TaskCategory, string[]> CategoryKeywords = new()
    {
        [TaskCategory.Code] = [
            "code", "function", "class", "method", "bug", "fix", "implement",
            "refactor", "debug", "compile", "build", "test", "unit test",
            "api", "endpoint", "database", "query", "sql", "script",
            "javascript", "typescript", "python", "csharp", "c#", "java",
            "react", "vue", "angular", "node", "dotnet", ".net",
            "git", "commit", "merge", "branch", "pull request", "pr",
            "error", "exception", "stack trace", "null", "undefined"
        ],
        [TaskCategory.Planning] = [
            "plan", "design", "architect", "architecture", "strategy",
            "roadmap", "milestone", "phase", "approach", "solution",
            "system design", "how should", "what's the best way",
            "trade-off", "tradeoff", "pros and cons", "compare approaches",
            "requirements", "specification", "spec"
        ],
        [TaskCategory.Writing] = [
            "write", "draft", "document", "documentation", "readme",
            "blog", "article", "post", "email", "message", "letter",
            "essay", "report", "summary", "summarize", "explain",
            "describe", "elaborate", "expand on"
        ],
        [TaskCategory.Analysis] = [
            "analyze", "analyse", "evaluate", "assess", "review",
            "compare", "contrast", "examine", "investigate", "audit",
            "benchmark", "measure", "metrics", "performance",
            "what do you think", "opinion on", "thoughts on"
        ],
        [TaskCategory.Quick] = [
            "what is", "what's", "who is", "when did", "where is",
            "how many", "how much", "define", "definition",
            "quick question", "simple question", "just wondering"
        ],
        [TaskCategory.Reasoning] = [
            "calculate", "compute", "solve", "prove", "derive",
            "logic", "logical", "reason", "reasoning", "think through",
            "step by step", "work through", "math", "equation",
            "algorithm", "complexity", "optimize"
        ]
    };

    private static readonly string[] HighStakesKeywords = [
        "important", "critical", "production", "deploy", "release",
        "security", "authentication", "authorization", "payment",
        "customer data", "pii", "sensitive", "compliance"
    ];

    private static readonly string[] PrivateKeywords = [
        "private", "confidential", "secret", "internal only",
        "do not share", "sensitive", "personal", "local only"
    ];

    public TaskClassification Classify(string message)
    {
        var lowerMessage = message.ToLowerInvariant();
        var detectedKeywords = new List<string>();
        var categoryScores = new Dictionary<TaskCategory, int>();

        // Score each category based on keyword matches
        foreach (var (category, keywords) in CategoryKeywords)
        {
            var score = 0;
            foreach (var keyword in keywords)
            {
                if (lowerMessage.Contains(keyword))
                {
                    score++;
                    detectedKeywords.Add(keyword);
                }
            }
            if (score > 0)
            {
                categoryScores[category] = score;
            }
        }

        // Check for code patterns (file extensions, code blocks)
        if (CodePatternRegex().IsMatch(message))
        {
            categoryScores.TryGetValue(TaskCategory.Code, out var codeScore);
            categoryScores[TaskCategory.Code] = codeScore + 3;
            detectedKeywords.Add("[code pattern]");
        }

        // Check for private/sensitive content
        var isPrivate = PrivateKeywords.Any(k => lowerMessage.Contains(k));
        if (isPrivate)
        {
            detectedKeywords.Add("[private]");
        }

        // Check for high-stakes indicators
        var isHighStakes = HighStakesKeywords.Any(k => lowerMessage.Contains(k));

        // Determine primary category
        TaskCategory primaryCategory;
        double confidence;
        List<TaskCategory>? secondaryCategories = null;

        if (isPrivate)
        {
            primaryCategory = TaskCategory.Private;
            confidence = 0.9;
        }
        else if (categoryScores.Count == 0)
        {
            primaryCategory = TaskCategory.General;
            confidence = 0.5;
        }
        else
        {
            var sorted = categoryScores.OrderByDescending(kv => kv.Value).ToList();
            primaryCategory = sorted[0].Key;

            var totalScore = sorted.Sum(kv => kv.Value);
            confidence = Math.Min(0.95, 0.5 + (sorted[0].Value / (double)totalScore) * 0.45);

            if (sorted.Count > 1 && sorted[1].Value >= sorted[0].Value * 0.5)
            {
                secondaryCategories = sorted.Skip(1)
                    .Where(kv => kv.Value >= sorted[0].Value * 0.3)
                    .Select(kv => kv.Key)
                    .ToList();
            }
        }

        return new TaskClassification
        {
            Category = primaryCategory,
            Confidence = confidence,
            SecondaryCategories = secondaryCategories,
            DetectedKeywords = detectedKeywords.Count > 0 ? detectedKeywords.Distinct().ToList() : null,
            IsHighStakes = isHighStakes
        };
    }

    [GeneratedRegex(@"\.(cs|js|ts|py|java|go|rs|cpp|c|h|jsx|tsx|vue|sql|json|yaml|yml|xml|html|css|scss|md)\b|```\w*\n", RegexOptions.IgnoreCase)]
    private static partial Regex CodePatternRegex();
}
