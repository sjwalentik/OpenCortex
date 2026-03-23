using System.Text.Json;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Tools.Memory;

public sealed class MemoryToolDefinitions : IToolDefinitionProvider
{
    public IReadOnlyList<ToolDefinition> GetToolDefinitions() =>
    [
        SaveMemory,
        RecallMemories,
        ForgetMemory
    ];

    public static ToolDefinition SaveMemory => ToolDefinition.FromFunction(
        name: "save_memory",
        description: "Save an important fact, decision, preference, or learning for future recall.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "content": {
                    "type": "string",
                    "description": "The memory content to save."
                },
                "category": {
                    "type": "string",
                    "enum": ["fact", "decision", "preference", "learning"],
                    "description": "Category of memory."
                },
                "confidence": {
                    "type": "string",
                    "enum": ["high", "medium", "low"],
                    "default": "medium",
                    "description": "How confident the agent is in this memory."
                },
                "tags": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Optional tags for memory lookup."
                }
            },
            "required": ["content", "category"]
        }
        """));

    public static ToolDefinition RecallMemories => ToolDefinition.FromFunction(
        name: "recall_memories",
        description: "Search saved memories from past conversations.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "What to search for in saved memories."
                },
                "category": {
                    "type": "string",
                    "enum": ["fact", "decision", "preference", "learning"],
                    "description": "Optional memory category filter."
                },
                "limit": {
                    "type": "integer",
                    "default": 5,
                    "description": "Maximum number of memories to return."
                }
            },
            "required": ["query"]
        }
        """));

    public static ToolDefinition ForgetMemory => ToolDefinition.FromFunction(
        name: "forget_memory",
        description: "Delete a saved memory that is no longer accurate or useful.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "memory_path": {
                    "type": "string",
                    "description": "Canonical path of the memory to delete, for example memories/fact/abc123.md."
                },
                "reason": {
                    "type": "string",
                    "description": "Optional reason the memory is being removed."
                }
            },
            "required": ["memory_path"]
        }
        """));
}
