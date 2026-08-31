// Copyright (c) ANcpLua. Licensed under the MIT License.

using Microsoft.Agents.AI;

namespace ANcpLua.Agents.Evaluation;

/// <summary>
/// One fail-closed way to assert that an agent called the right tools, collapsing the framework's four
/// fragmented entrypoints — <see cref="EvalChecks.ToolCallsPresent"/>,
/// <see cref="EvalChecks.ToolCalledCheck(ToolCalledMode, string[])"/>, and
/// <see cref="EvalChecks.ToolCallArgsMatch"/> — into a single value with consistent semantics.
/// </summary>
/// <remarks>
/// The framework's <see cref="EvalChecks.ToolCallArgsMatch"/> <em>passes</em> when an item carries no
/// expected tool calls — a vacuous pass that reports correctness which was never checked.
/// <see cref="WithArguments"/> inverts that default: an argument expectation that has nothing to compare
/// against fails closed.
/// </remarks>
public sealed class ToolExpectation
{
    private readonly EvalCheck _check;

    private ToolExpectation(EvalCheck check) => this._check = check;

    /// <summary>Requires that the agent made at least one tool call.</summary>
    public static ToolExpectation Present() => new(EvalChecks.ToolCallsPresent());

    /// <summary>Requires that every named tool was called.</summary>
    /// <param name="toolNames">The tool names that must all appear.</param>
    public static ToolExpectation AllOf(params string[] toolNames) =>
        new(EvalChecks.ToolCalledCheck(ToolCalledMode.All, Require(toolNames)));

    /// <summary>Requires that at least one of the named tools was called.</summary>
    /// <param name="toolNames">The candidate tool names.</param>
    public static ToolExpectation AnyOf(params string[] toolNames) =>
        new(EvalChecks.ToolCalledCheck(ToolCalledMode.Any, Require(toolNames)));

    /// <summary>
    /// Requires that the expected tools were called with matching arguments (subset match). Fails closed
    /// when there is nothing to compare against.
    /// </summary>
    /// <remarks>
    /// The same expectation applies to every item unless an item carries its own
    /// <see cref="EvalItem.ExpectedToolCalls"/> (for example via <c>EvaluationSuite.ExpectingToolCalls</c>,
    /// one array per query), which takes precedence. Items are never mutated.
    /// </remarks>
    /// <param name="expected">The expected tool calls and their arguments.</param>
    public static ToolExpectation WithArguments(params ExpectedToolCall[] expected)
    {
        if (expected is null || expected.Length == 0)
        {
            throw new ArgumentException("WithArguments requires at least one expected tool call.", nameof(expected));
        }

        var match = EvalChecks.ToolCallArgsMatch();
        return new ToolExpectation(item =>
        {
            // An item-level expectation (for example from ExpectingToolCalls) wins; this
            // expectation's own list is the fallback. The comparison runs on a clone so the
            // caller's item is never mutated by evaluation.
            IReadOnlyList<ExpectedToolCall>? effective = item.ExpectedToolCalls ?? expected;
            if (effective.Count == 0)
            {
                return new EvalCheckResult(false, "argument expectation configured but no expected tool calls to compare", "tool_call_args_match");
            }

            var probe = new EvalItem(item.Query, item.Response, item.Conversation)
            {
                Tools = item.Tools,
                Context = item.Context,
                ExpectedOutput = item.ExpectedOutput,
                RawResponse = item.RawResponse,
                Splitter = item.Splitter,
                ExpectedToolCalls = effective,
            };

            return match(probe);
        });
    }

    /// <summary>Gets the underlying check function.</summary>
    internal EvalCheck ToCheck() => this._check;

    private static string[] Require(string[] toolNames) =>
        toolNames is { Length: > 0 }
            ? toolNames
            : throw new ArgumentException("Specify at least one tool name.", nameof(toolNames));
}
